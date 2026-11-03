"""HTTP acceptance tests. Run ONLY against a disposable Development database.
Usage: python tests/smoke_test.py --base http://localhost:5081 --output tests/results.json
The suite creates/updates/deletes synthetic data and tests account lockout.
"""
import argparse
import datetime as dt
import html
import json
import re
import sys
import time
from pathlib import Path
import requests

parser = argparse.ArgumentParser()
parser.add_argument('--base', default='http://localhost:5081')
parser.add_argument('--output', default='tests/results.json')
args = parser.parse_args()
BASE = args.base.rstrip('/')
PASSWORD = 'Acxiom@2026!'
RUN = str(int(time.time()))[-8:]
TODAY = dt.date.today()
FUTURE = (TODAY + dt.timedelta(days=14)).isoformat()
PAST = (TODAY - dt.timedelta(days=1)).isoformat()
RESULTS = []

def verify(name, condition, detail=''):
    result = {'name': name, 'passed': bool(condition), 'detail': str(detail)[:450] if not condition else ''}
    RESULTS.append(result)
    print(('PASS ' if condition else 'FAIL ') + name + (': ' + result['detail'] if not condition else ''), flush=True)
    return bool(condition)

def expect(name, response, statuses):
    if isinstance(statuses, int): statuses = [statuses]
    return verify(name, response.status_code in statuses, f'HTTP {response.status_code}: {response.text[:300]}')

def client():
    s = requests.Session()
    return s

def call(s, method, path, **kwargs):
    return s.request(method, BASE + path, timeout=30, allow_redirects=False, **kwargs)

def csrf(s):
    r = call(s, 'GET', '/api/auth/csrf')
    r.raise_for_status()
    data = r.json()
    token = data.get('token') or data.get('requestToken')
    if not token: raise RuntimeError('No CSRF token returned: ' + r.text[:200])
    return {data.get('headerName', 'X-CSRF-TOKEN'): token}

def write(s, method, path, data=None):
    return call(s, method, path, json=data, headers=csrf(s))

def login(email, password=PASSWORD, name=None):
    s = client()
    r = write(s, 'POST', '/api/auth/login', {'email': email, 'password': password})
    expect(name or 'Login ' + email, r, 200)
    s.login_cookie_header = r.headers.get('Set-Cookie', '')
    return s

def items(s, path):
    r = call(s, 'GET', path)
    r.raise_for_status()
    data = r.json()
    return data if isinstance(data, list) else data['items']

def form_token(s, path):
    r = call(s, 'GET', path)
    r.raise_for_status()
    match = re.search(r'name="__RequestVerificationToken"[^>]*value="([^"]+)"', r.text)
    if not match: raise RuntimeError('No form token on ' + path)
    return html.unescape(match.group(1))

def main():
    anon = client()
    r = call(anon, 'GET', '/')
    verify('Anonymous dashboard redirects to login', r.status_code == 302 and '/Account/Login' in r.headers.get('Location', ''))
    expect('Anonymous customer API returns 401', call(anon, 'GET', '/api/customers'), 401)
    expect('Anonymous user administration is protected', call(anon, 'GET', '/Users'), 302)
    expect('API login without CSRF is rejected', call(anon, 'POST', '/api/auth/login', json={'email':'admin@acxiom.local','password':PASSWORD}), 400)
    expect('Invalid login is rejected', write(anon, 'POST', '/api/auth/login', {'email':'nobody@example.test','password':'Wrong@2026!'}), 401)
    admin = login('admin@acxiom.local')
    manager = login('manager@acxiom.local')
    sales = login('sales@acxiom.local')
    sales2 = login('sales2@acxiom.local')
    owners = items(admin, '/api/owners')
    own1 = items(sales, '/api/owners')[0]['id']
    own2 = items(sales2, '/api/owners')[0]['id']
    verify('Sales sees only its own assignment option', len(items(sales, '/api/owners')) == 1 and own1 != own2)
    verify('Admin sees the active sales team', own1 in [x['id'] for x in owners] and own2 in [x['id'] for x in owners])

    for path in ['/', '/Crm/Index?kind=customers', '/Crm/Index?kind=leads', '/Crm/Index?kind=opportunities', '/Crm/Index?kind=followups', '/Crm/Index?kind=activities', '/Users', '/Audit']:
        expect('Admin page renders: ' + path, call(admin, 'GET', path), 200)
    for kind in ['customers','leads','opportunities','followups','activities']:
        expect('Create form renders: ' + kind, call(admin, 'GET', '/Crm/Create?kind=' + kind), 200)
    for typ in ['customers','leads','opportunities','followups','pipeline','conversion','activity','audit']:
        expect('Report renders: ' + typ, call(admin, 'GET', '/Reports?type=' + typ), 200)
    expect('Manager pipeline access', call(manager, 'GET', '/Reports?type=pipeline'), 200)
    expect('Manager cannot administer users', call(manager, 'GET', '/Users'), [302,403])
    expect('Sales cannot view audit log', call(sales, 'GET', '/Audit'), [302,403])
    expect('Sales cannot view audit report', call(sales, 'GET', '/Reports?type=audit'), [302,403])

    cust = {'name':'Acceptance '+RUN, 'email':f'acceptance{RUN}@example.test', 'phone':'9'+RUN+'1', 'companyName':'Test Company', 'status':'Active', 'ownerId':own1}
    expect('Missing required customer fields', write(admin,'POST','/api/customers',{'ownerId':own1}),400)
    expect('Invalid customer email', write(admin,'POST','/api/customers',{**cust,'email':'not-an-email'}),400)
    expect('Invalid customer phone', write(admin,'POST','/api/customers',{**cust,'phone':'123'}),400)
    expect('Overlong customer name', write(admin,'POST','/api/customers',{**cust,'name':'a'*151}),400)
    expect('Customer creation without CSRF', call(admin,'POST','/api/customers',json=cust),400)
    r = write(admin,'POST','/api/customers',cust)
    expect('Create valid customer',r,201)
    customer = r.json(); cid = customer['id']
    verify('Customer DTO excludes internal credentials', not any(k.lower() in r.text.lower() for k in ['passwordHash','securityStamp','concurrencyStamp']))
    expect('Duplicate normalized customer email',write(admin,'POST','/api/customers',{**cust,'email':cust['email'].upper(),'phone':'9'+RUN+'2'}),[400,409])
    expect('Duplicate customer phone',write(admin,'POST','/api/customers',{**cust,'email':f'dupe{RUN}@example.test'}),[400,409])
    expect('Sales can read its assigned customer',call(sales,'GET',f'/api/customers/{cid}'),200)
    expect('Other sales cannot read customer by ID',call(sales2,'GET',f'/api/customers/{cid}'),[403,404])
    expect('Other sales cannot edit customer',write(sales2,'PUT',f'/api/customers/{cid}',cust),[403,404])
    expect('Other sales cannot delete customer',write(sales2,'DELETE',f'/api/customers/{cid}'),[403,404])
    expect('Sales cannot assign to another owner',write(sales,'PUT',f'/api/customers/{cid}',{**cust,'ownerId':own2}),403)
    verify('Sales list contains only assigned records',all(x['ownerId']==own1 for x in items(sales,'/api/customers?pageSize=100')))
    expect('Manager can read team customer',call(manager,'GET',f'/api/customers/{cid}'),200)
    cust['notes'] = 'Updated by HTTP acceptance test'
    expect('Update customer',write(admin,'PUT',f'/api/customers/{cid}',cust),200)
    verify('Customer update persisted',call(admin,'GET',f'/api/customers/{cid}').json()['notes']==cust['notes'])
    for action in ['Details','Edit','Delete']:
        expect('Customer ' + action + ' page renders',call(admin,'GET',f'/Crm/{action}?kind=customers&id={cid}'),200)
    r = call(admin,'GET',f'/api/customers?q={RUN}&pageSize=5')
    expect('Search and pagination API',r,200)

    opp = {'name':'Test opportunity '+RUN,'customerId':cid,'amount':25000,'stage':'Qualification','status':'Open','probability':35,'expectedCloseDate':FUTURE,'ownerId':own1}
    for name,patch in [('Zero amount',{'amount':0}),('Negative amount',{'amount':-1}),('Probability above 100',{'probability':101}),('Probability below zero',{'probability':-1}),('Past expected close date',{'expectedCloseDate':PAST}),('Nonexistent customer reference',{'customerId':2147483647}),('Invalid stage',{'stage':'Hacked'}),('Money precision',{'amount':1.999})]:
        expect('Opportunity rejects '+name,write(admin,'POST','/api/opportunities',{**opp,**patch}),400)
    expect('Sales cannot reference another owner customer',write(sales2,'POST','/api/opportunities',{**opp,'ownerId':own2}),400)
    r = write(admin,'POST','/api/opportunities',opp); expect('Create opportunity',r,201); oid=r.json()['id']
    expect('Pipeline JSON API',call(manager,'GET','/api/reports/pipeline'),200)
    opp.update(stage='Won',status='Won',probability=60)
    r=write(admin,'PUT',f'/api/opportunities/{oid}',opp); expect('Close opportunity as won',r,200)
    verify('Won probability normalized to 100',r.json().get('probability')==100)
    verify('Won opportunity has recorded outcome date',bool(r.json().get('closedDate')))

    follow = {'subject':'Follow-up '+RUN,'type':'Call','status':'Planned','followUpDate':FUTURE,'customerId':cid,'ownerId':own1}
    expect('Reject past new follow-up',write(admin,'POST','/api/followups',{**follow,'followUpDate':PAST}),400)
    expect('Reject missing follow-up relationship',write(admin,'POST','/api/followups',{**follow,'customerId':None}),400)
    r=write(admin,'POST','/api/followups',follow); expect('Create follow-up',r,201); fid=r.json()['id']
    follow['followUpDate']=(TODAY+dt.timedelta(days=20)).isoformat()
    expect('Reschedule follow-up',write(admin,'PUT',f'/api/followups/{fid}',follow),200)
    follow['status']='Completed'
    expect('Complete follow-up',write(admin,'PUT',f'/api/followups/{fid}',follow),200)
    activity={'subject':'Meeting '+RUN,'type':'Meeting','status':'Planned','activityDate':TODAY.isoformat(),'customerId':cid,'ownerId':own1,'description':'Acceptance activity'}
    r=write(admin,'POST','/api/activities',activity);expect('Create activity',r,201);aid=r.json()['id']
    activity['status']='Completed'
    expect('Complete activity',write(admin,'PUT',f'/api/activities/{aid}',activity),200)

    lead={'name':'Lead '+RUN,'email':f'lead{RUN}@example.test','phone':'8'+RUN+'1','companyName':'Lead Company','status':'New','source':'Website','priority':'High','expectedValue':50000,'ownerId':own1}
    expect('Reject invalid lead status',write(admin,'POST','/api/leads',{**lead,'status':'Hacked'}),400)
    expect('Reject negative expected value',write(admin,'POST','/api/leads',{**lead,'expectedValue':-5}),400)
    r=write(admin,'POST','/api/leads',lead);expect('Create new lead',r,201);lid=r.json()['id']
    expect('Reject conversion before qualification',write(admin,'POST',f'/api/leads/{lid}/convert'),409)
    expect('Reject skipped lead transition',write(admin,'PUT',f'/api/leads/{lid}',{**lead,'status':'Qualified'}),400)
    for status in ['Contacted','Qualified']:
        lead['status']=status
        expect('Lead transition to '+status,write(admin,'PUT',f'/api/leads/{lid}',lead),200)
    r=write(admin,'POST',f'/api/leads/{lid}/convert');expect('Convert qualified lead into customer',r,[200,201])
    converted=call(admin,'GET',f'/api/leads/{lid}').json()
    verify('Converted lead links customer',converted['status']=='Converted' and bool(converted['customerId']))
    expect('Reject repeated conversion',write(admin,'POST',f'/api/leads/{lid}/convert'),409)
    expect('Converted lead history cannot be deleted',write(admin,'DELETE',f'/api/leads/{lid}'),409)

    for kind,rid in [('opportunities',oid),('followups',fid),('activities',aid),('leads',lid)]:
        for action in ['Details','Edit','Delete']:
            expect(kind+' '+action+' page renders',call(admin,'GET',f'/Crm/{action}?kind={kind}&id={rid}'),200)
    expect('Related customer deletion is blocked',write(admin,'DELETE',f'/api/customers/{cid}'),409)
    r=call(admin,'GET',f'/Reports?type=customers&query={RUN}&export=true')
    expect('Filtered CSV export',r,200)
    verify('CSV has header and created customer', 'Customer' in r.text and cust['email'] in r.text and 'text/csv' in r.headers.get('Content-Type',''))
    r=call(sales2,'GET',f'/Reports?type=customers&query={RUN}&export=true')
    verify('CSV export honors sales ownership',cust['email'] not in r.text)
    expect('Reject reversed report date range',call(admin,'GET',f'/Reports?type=customers&from={FUTURE}&to={PAST}&export=true'),400)
    r=call(admin,'GET',f'/Audit?recordId={cid}')
    verify('Audit page contains create/update actions','Create' in r.text and 'Update' in r.text)
    verify('Audit output does not contain demo password',PASSWORD not in r.text)

    cust['name']='<script>alert(1)</script>'
    write(admin,'PUT',f'/api/customers/{cid}',cust)
    r=call(admin,'GET',f'/Crm/Details?kind=customers&id={cid}')
    verify('Razor encodes stored HTML','<script>alert(1)</script>' not in r.text and '&lt;script&gt;' in r.text)
    cust['name']='=2+3'
    write(admin,'PUT',f'/api/customers/{cid}',cust)
    r=call(admin,'GET',f'/Reports?type=customers&query={RUN}&export=true')
    verify('CSV formula injection is neutralized',"'=2+3" in r.text)
    for kind,rid in [('activities',aid),('followups',fid),('opportunities',oid),('customers',cid)]:
        expect('Delete '+kind,write(admin,'DELETE',f'/api/{kind}/{rid}'),[200,204])
        expect('Deleted '+kind+' returns 404',call(admin,'GET',f'/api/{kind}/{rid}'),404)

    reg=client(); token=form_token(reg,'/Account/Register')
    fields={'Name':'Registered Test '+RUN,'Email':f'reg{RUN}@example.test','Password':'weak','ConfirmPassword':'weak','__RequestVerificationToken':token}
    r=call(reg,'POST','/Account/Register',data=fields)
    verify('Registration rejects weak password',r.status_code==200 and ('10' in r.text or 'password' in r.text.lower()))
    fields.update(Password=PASSWORD,ConfirmPassword=PASSWORD,Role='Admin')
    fields['__RequestVerificationToken']=form_token(reg,'/Account/Register')
    expect('Registration succeeds with valid policy',call(reg,'POST','/Account/Register',data=fields),302)
    expect('Registered account cannot self-select Admin',call(reg,'GET','/Users'),[302,403])
    verify('New registered sales user has no others records',len(items(reg,'/api/customers'))==0)
    expect('Logout ends API session',write(reg,'POST','/api/auth/logout'),[200,204])
    expect('Logged-out API is protected',call(reg,'GET','/api/customers'),401)

    lock=client()
    for _ in range(5):
        write(lock,'POST','/api/auth/login',{'email':f'reg{RUN}@example.test','password':'Wrong@2026!'})
    expect('Five failures lock account even with correct password',write(lock,'POST','/api/auth/login',{'email':f'reg{RUN}@example.test','password':PASSWORD}),[401,423])

    r=call(admin,'GET','/Users')
    verify('User administration excludes password hashes','PasswordHash' not in r.text and 'SecurityStamp' not in r.text)
    verify('Session cookie is HttpOnly','AcxiomCRM.Session=' in admin.login_cookie_header and 'httponly' in admin.login_cookie_header.lower())
    r=call(admin,'GET','/')
    verify('Browser security headers set',r.headers.get('X-Content-Type-Options')=='nosniff' and r.headers.get('X-Frame-Options')=='DENY')
    for asset in ['/lib/bootstrap.min.css','/lib/chart.umd.js','/lib/jquery.validate.min.js','/lib/jquery.validate.unobtrusive.min.js']:
        expect('Offline asset available: '+asset,call(admin,'GET',asset),200)

try:
    main()
except Exception as exc:
    verify('Test suite completed without unexpected exception',False,str(exc))
finally:
    summary={'testedAtUtc':dt.datetime.now(dt.timezone.utc).isoformat(),'baseUrl':BASE,'total':len(RESULTS),'passed':sum(x['passed'] for x in RESULTS),'failed':sum(not x['passed'] for x in RESULTS),'checks':RESULTS}
    path=Path(args.output);path.parent.mkdir(parents=True,exist_ok=True);path.write_text(json.dumps(summary,indent=2),encoding='utf-8')
    print(f"\n{summary['passed']}/{summary['total']} passed. Results: {path}")
    sys.exit(1 if summary['failed'] else 0)
