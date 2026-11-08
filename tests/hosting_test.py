"""Exercise Production hosting and restart persistence with simulated managed ingress.
Run after dotnet build -c Release. This creates only disposable local data.
"""
import argparse
import json
import os
from pathlib import Path
import secrets
import socket
import subprocess
import tempfile
import time
import requests

p = argparse.ArgumentParser()
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', default='TestResults/hosting.json')
p.add_argument('--provider', choices=['railway', 'render'], default='railway')
p.add_argument('--database', choices=['sqlite', 'postgres'], default='sqlite')
a = p.parse_args()
root = Path(__file__).resolve().parents[1]
(root / 'App_Data').mkdir(exist_ok=True)
checks = []
hostname = 'acxiom-test.onrender.com' if a.provider == 'render' else 'acxiom-test.up.railway.app'
probe_host = hostname if a.provider == 'render' else 'healthcheck.railway.app'
volume_key = 'Hosting__DataPath' if a.provider == 'render' else 'RAILWAY_VOLUME_MOUNT_PATH'

def check(name, condition):
    checks.append({'name': name, 'passed': bool(condition)})
    print(('PASS ' if condition else 'FAIL ') + name, flush=True)
    if not condition:
        raise AssertionError(name)

with tempfile.TemporaryDirectory(prefix='hosting-', dir=root / 'App_Data') as scratch:
    volume = Path(scratch)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1', 0))
        port = sock.getsockname()[1]
    base = f'http://127.0.0.1:{port}'
    env = os.environ.copy()
    for key in ('RENDER', 'RAILWAY_SERVICE_ID', 'RAILWAY_VOLUME_MOUNT_PATH', 'Hosting__DataPath', 'ASPNETCORE_FORWARDEDHEADERS_ENABLED'):
        env.pop(key, None)
    env.update({
        'ASPNETCORE_ENVIRONMENT': 'Production', 'DOTNET_ENVIRONMENT': 'Production',
        'RAILWAY_SERVICE_ID': 'local-hosting-test', 'RAILWAY_VOLUME_MOUNT_PATH': scratch,
        'RAILWAY_PUBLIC_DOMAIN': 'acxiom-test.up.railway.app', 'Hosting__RailwayProxy': 'true',
        'Bootstrap__Email': 'production-test@example.test', 'Bootstrap__Name': 'Production test admin',
        'Bootstrap__Password': secrets.token_urlsafe(24) + 'Aa9!', 'PORT': str(port),
        'AllowedHosts': 'localhost;127.0.0.1', 'Authentication__AllowRegistration': 'false',
        'Logging__LogLevel__Default': 'Error', 'Database__Provider': 'Sqlite'
    })
    if a.provider == 'render':
        for key in ('RAILWAY_SERVICE_ID', 'RAILWAY_VOLUME_MOUNT_PATH', 'RAILWAY_PUBLIC_DOMAIN', 'Hosting__RailwayProxy'):
            env.pop(key, None)
        env.update({'RENDER': 'true', 'RENDER_EXTERNAL_HOSTNAME': hostname, 'Hosting__RenderProxy': 'true', volume_key: scratch})
    if a.database == 'postgres':
        env.update({'Database__Provider': 'PostgreSQL', 'ConnectionStrings__DefaultConnection': os.environ['TEST_POSTGRES_CONNECTION']})
        env.pop(volume_key, None)
    cmd = [a.dotnet, str(root / 'bin/Release/net10.0/AcxiomCRM.dll')]
    proc = None
    log = (volume / 'server.log').open('w', encoding='utf-8')

    def start():
        global proc
        proc = subprocess.Popen(cmd, cwd=root, env=env, stdout=log, stderr=subprocess.STDOUT)
        for _ in range(100):
            if proc.poll() is not None:
                raise RuntimeError('Server exited; inspect the hosting test log.')
            try:
                r = requests.get(base + '/healthz', headers={'Host': probe_host}, timeout=1)
                if r.status_code == 200: return r
            except requests.RequestException:
                pass
            time.sleep(.1)
        raise RuntimeError('Server did not become ready.')

    def stop():
        global proc
        if proc and proc.poll() is None:
            proc.terminate()
            try: proc.wait(timeout=10)
            except subprocess.TimeoutExpired: proc.kill(); proc.wait()

    def request(session, method, path, **kwargs):
        # The local test sends HTTP behind a simulated HTTPS edge. Replay Secure
        # cookies explicitly because requests correctly withholds them on HTTP.
        headers = {'Host': hostname, 'X-Forwarded-Proto': 'https'}
        headers.update(kwargs.pop('headers', {}))
        if session.cookies:
            headers['Cookie'] = '; '.join(c.name + '=' + c.value for c in session.cookies)
        return session.request(method, base + path, headers=headers, timeout=10, allow_redirects=False, **kwargs)

    try:
        r = start()
        check('Readiness accepts hosting probe hostname', r.status_code == 200 and r.json() == {'status':'ok'})
        if a.database == 'sqlite':
            check('SQLite is stored on mounted volume', (volume / 'acxiom.db').is_file())
        else:
            check('PostgreSQL hosting starts without local persistent storage', not (volume / 'acxiom.db').exists())
        check('Unknown Host is rejected', requests.get(base + '/healthz', headers={'Host':'attacker.example'}, timeout=5).status_code == 400)
        r = requests.get(base + '/Account/Login', headers={'Host':hostname}, allow_redirects=False, timeout=5)
        check('Plain HTTP redirects to public HTTPS port 443', r.status_code == 307 and r.headers['Location'] == f'https://{hostname}/Account/Login')
        s = requests.Session()
        r = request(s, 'GET', '/Account/Login')
        check('Forwarded HTTPS renders login without redirect loop', r.status_code == 200)
        check('Disabled registration does not advertise a broken signup link', 'href="/Account/Register"' not in r.text)
        check('Production responses carry HSTS', 'Strict-Transport-Security' in r.headers)
        check('Public registration is disabled', request(s, 'GET', '/Account/Register').status_code == 404)
        check('Anonymous CRM remains protected', request(s, 'GET', '/api/customers').status_code == 401)
        token = request(s, 'GET', '/api/auth/csrf').json()['token']
        r = request(s, 'POST', '/api/auth/login', headers={'X-CSRF-TOKEN':token}, json={'email':env['Bootstrap__Email'],'password':env['Bootstrap__Password']})
        check('Configured production administrator can log in', r.status_code == 200 and r.json()['role'] == 'Admin')
        cookie = r.headers.get('Set-Cookie','').lower()
        check('Production session is Secure and HttpOnly', 'secure' in cookie and 'httponly' in cookie)
        check('Authenticated session accesses CRM', request(s,'GET','/api/customers').status_code == 200)
        check('Development demo data is absent in production', request(s,'GET','/api/customers').json()['total'] == 0)
        if a.database == 'sqlite':
            check('Data Protection keys are persisted', any((volume/'keys').glob('key-*.xml')))
        else:
            import psycopg
            with psycopg.connect(os.environ['TEST_POSTGRES_DSN'], autocommit=True) as pg:
                check('Data Protection keys are persisted in PostgreSQL', pg.execute('SELECT COUNT(*) FROM "DataProtectionKeys"').fetchone()[0] > 0)
                check('Production login creates durable audit records', pg.execute('SELECT COUNT(*) FROM "AuditLogs"').fetchone()[0] > 0)
                for statement in ('UPDATE "AuditLogs" SET "Action" = \'tampered\'', 'DELETE FROM "AuditLogs"'):
                    blocked = False
                    try:
                        with pg.transaction():
                            pg.execute(statement)
                    except psycopg.errors.RaiseException:
                        blocked = True
                    check('PostgreSQL blocks direct audit ' + statement.split()[0], blocked)
        stop()
        env.pop('Bootstrap__Password')
        start()
        r = request(s,'GET','/api/auth/me')
        check('Administrator and session survive restart without bootstrap secret', r.status_code == 200 and r.json()['email'] == 'production-test@example.test')
        stop()
        if a.database == 'sqlite':
            env.pop(volume_key)
        else:
            env.pop('ConnectionStrings__DefaultConnection')
        missing = subprocess.run(cmd, cwd=root, env=env, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=15)
        check('Cloud startup rejects missing persistent storage configuration', missing.returncode != 0)
    finally:
        stop()
        log.close()
        output = root / a.output
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(json.dumps({'passed':sum(c['passed'] for c in checks),'total':len(checks),'checks':checks}, indent=2), encoding='utf-8')

print(f'{len(checks)}/{len(checks)} hosting checks passed.')
