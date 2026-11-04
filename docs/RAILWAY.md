# Railway deployment

This repository contains the Railway deployment configuration. A successful GitHub build alone does not create a Railway project, volume or live URL.

## Service setup

1. Create a Railway project from `RithvikKuchi/ACXIOM-DRIVE`, branch `main`. Use the repository root and its Dockerfile.
2. Attach a persistent volume to the application service at **`/app/App_Data`** before deployment. The app requires a volume in Railway Production mode. The SQLite database and the ASP.NET Data Protection key ring are stored there together.
3. Configure the following service variables. Set passwords directly in Railway variables; do not put them in Git, screenshots or command-line arguments.

| Variable | Value |
|---|---|
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Hosting__RailwayProxy` | `true` |
| `RAILWAY_RUN_UID` | `0` (Railway mounts volumes as root) |
| `Authentication__AllowRegistration` | `false` |
| `Bootstrap__Email` | Your chosen administrator login email |
| `Bootstrap__Name` | Your administrator display name |
| `Bootstrap__Password` | A unique random password, at least 10 characters with upper/lowercase, number and symbol |

Railway supplies `PORT`, `RAILWAY_SERVICE_ID` and `RAILWAY_VOLUME_MOUNT_PATH`. Do not manually define the volume path variable in place of actually attaching a volume. The application binds to `PORT` on all interfaces.

4. Deploy. `railway.json` selects Dockerfile builds and the anonymous `/healthz` readiness endpoint. Schema and initial admin creation run at application startup, when the volume is mounted.
5. Generate a Railway public domain and target the application port. Redeploy if needed so `RAILWAY_PUBLIC_DOMAIN` is available to the application. It is included in the allowed-host list together with `healthcheck.railway.app`. Add custom domains explicitly through `AllowedHosts`, separated with semicolons.
6. Open the HTTPS URL and sign in with the bootstrap credentials. Production does **not** create the public Development demo accounts or sample sales records. Create your sales users under Users & roles, then assign records to them.
7. Once the administrator exists, remove `Bootstrap__Password` from Railway variables and redeploy. The existing account remains; credentials and roles are persisted in the volume. Set up volume backups before entering important data.

## Network and persistence decisions

- Railway terminates public TLS before the container. `Hosting__RailwayProxy=true` enables a Railway-specific single-hop forwarded-scheme policy only when the application is in Production and `RAILWAY_SERVICE_ID` exists. Do not expose this service through a separate raw TCP proxy or share its private network with untrusted services.
- Only `X-Forwarded-Proto` is processed for HTTPS detection. Forwarded client IP and forwarded host values are not trusted. Audit IPs and per-IP throttling therefore use the immediate peer address; Identity account lockout remains enforced per user.
- HTTPS redirection, Secure/HttpOnly authentication cookies, anti-forgery tokens and authorization remain enabled. Readiness responds over internal HTTP without redirecting and does not expose database contents or configuration.
- A persistent key ring allows authentication cookies to survive restarts. The volume contains both sensitive application data and keys; restrict access to the service and workspace administrators. This deployment uses filesystem access controls, not an external key-management service.
- Keep a single service replica with the SQLite volume. A volume deployment may have brief downtime. For multiple replicas, migrate to a shared database and shared protected key storage first.
- Do not mount a volume over `/app`: it would hide the published application files.

## Verify after deployment

- `/healthz` returns HTTP 200.
- `/` redirects anonymous visitors to sign-in, and `/api/customers` returns 401 without a session.
- HTTP public requests redirect to HTTPS.
- The configured administrator can sign in; Development demo credentials cannot.
- Create a sales user and a test customer, restart the service, and confirm the record and session still work.
- Review deployment logs and confirm the application uses the mounted database directory.

The repository also includes `python tests/hosting_test.py` (after a Release build) to simulate Railway ingress locally. It checks mounted SQLite storage, public/healthcheck host filtering, HTTPS redirects, Secure cookies, bootstrap login, disabled public registration, key persistence, session survival across a process restart, and rejection of a missing volume. These tests verify the application behavior; they do not establish that a Railway cloud deployment has occurred.

## References

- [Railway ASP.NET Core deployment](https://railway.com/guides/aspnet-core)
- [Railway volumes and permissions](https://docs.railway.com/volumes)
- [Railway healthchecks and hostname](https://docs.railway.com/deployments/healthchecks)
- [ASP.NET Core proxy handling](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0)
