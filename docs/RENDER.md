# Free hosting: Render + Neon

GitHub remains the source of truth: `RithvikKuchi/ACXIOM-DRIVE`, branch `main`. Render runs the entire ASP.NET Core application. Neon stores its PostgreSQL database, including accounts, records, audit logs and login encryption keys. No persistent disk or paid service is required.

## Free-tier limits

Keep both accounts on their **Free** plans. The root `render.yaml` uses `plan: free` and defines no disk or Render database. Render may sleep after inactivity, so the first visit can take longer. Free compute, storage and transfer quotas can suspend service when exhausted; do not enable paid upgrades. Render's own free PostgreSQL database expires after 30 days, so this setup uses Neon instead. Review provider limits periodically and keep backups.

## Setup

1. Connect Render to this GitHub repository. If it is private, grant access to this repository.
2. Create a **Neon Free** project in Singapore, PostgreSQL 17, with a dedicated empty database. This application creates its schema at first startup. Do not point it at another application's database.
3. In Neon, get the database connection information and format it as an Npgsql connection string:

   ```text
   Host=YOUR_NEON_HOST;Database=YOUR_DATABASE;Username=YOUR_ROLE;Password=YOUR_PASSWORD;SSL Mode=VerifyFull;Channel Binding=Require;Maximum Pool Size=10;Timeout=30
   ```

   Use a connection-string builder if a credential contains reserved characters. Store this as the **secret** `ConnectionStrings__DefaultConnection` in Render. Do not commit it or paste it into GitHub issues. Neon normally displays a PostgreSQL URL; the application expects the semicolon-separated Npgsql format above.

4. Open [Deploy to Render](https://dashboard.render.com/select-repo?type=blueprint&repo=https%3A%2F%2Fgithub.com%2FRithvikKuchi%2FACXIOM-DRIVE), or choose **New > Blueprint** and select `main` and `render.yaml`.
5. Enter the connection string, `Bootstrap__Email` (administrator login email), and `Bootstrap__Password` (unique, at least 10 characters with uppercase, lowercase, number and symbol). Confirm the service is **Free**, has **no disk**, and creates **no paid database**.
6. Deploy and wait for **Live**. Open the HTTPS `onrender.com` URL. `/healthz` should return 200. Sign in with the administrator credentials. Production has no public demo accounts or example records.
7. Create a sales user and test record, restart the service, and verify that the record and login session survive. Remove `Bootstrap__Password` from Render after the first administrator exists; the persisted account remains.

## Configuration and persistence

- `Database__Provider=PostgreSQL` selects the cloud database. Missing or invalid database settings fail startup instead of falling back to ephemeral SQLite.
- Render provides `RENDER=true` and `RENDER_EXTERNAL_HOSTNAME`. The app listens on `PORT` and permits the generated hostname. Add any custom domain explicitly to `AllowedHosts`.
- `Hosting__RenderProxy=true` accepts one forwarded HTTPS scheme header from Render's managed ingress. Forwarded host and client IP are not trusted. Keep private-network peers trusted. Audit/throttling IPs use the immediate peer.
- HTTPS, Secure/HttpOnly cookies, CSRF validation and role/owner restrictions remain enabled. Public registration is disabled.
- The remote `DataProtectionKeys` table stores the authentication key ring. Database access therefore includes access to sensitive authentication material; keep credentials private. TLS certificate validation protects database connections in the documented setup.
- The PostgreSQL audit trigger rejects direct updates/deletes, and the application also enforces append-only audits.
- GitHub checks gate automatic Render updates. No paid upgrades are requested by the blueprint.
- Local Development still uses SQLite; the optional Railway/Render SQLite mode still requires a persistent volume. Neither the local database nor the temporary public demo's data is automatically copied into Neon.
- Schema initialization uses `EnsureCreated` for a fresh database. Future schema changes need a reviewed migration; do not expect automatic conversion of an existing database schema.

## Verification

GitHub Actions runs the complete HTTP acceptance suite on both SQLite and PostgreSQL. It separately tests production login, host filtering, HTTPS, disabled public registration, key persistence and session survival across process restarts. PostgreSQL checks additionally verify database audit triggers and operation without local persistent storage.

Local SQLite hosting checks:

```text
dotnet build -c Release
python tests/hosting_test.py --provider render
```

For PostgreSQL tests, use only a disposable, empty database and set `TEST_POSTGRES_CONNECTION` to its Npgsql string and `TEST_POSTGRES_DSN` to its PostgreSQL DSN. Install `psycopg[binary]`, then run `python tests/hosting_test.py --provider render --database postgres`. The tests create users/audit records and attempt blocked audit mutations. Never run the acceptance tests against the live CRM database.

## References

- [Render free hosting limits](https://render.com/docs/free)
- [Render Blueprint fields](https://render.com/docs/blueprint-spec)
- [Neon Free plan](https://neon.com/pricing)
- [Npgsql PostgreSQL provider](https://www.npgsql.org/efcore/)
- [Database-backed Data Protection keys](https://learn.microsoft.com/aspnet/core/security/data-protection/implementation/key-storage-providers)
