# Deploy AcxiomCRM on Render

GitHub remains the source of truth: `RithvikKuchi/ACXIOM-DRIVE`, branch `main`. Render runs the complete ASP.NET Core application and its SQLite database; GitHub Pages cannot run this server application.

## Cost and account requirements

The root `render.yaml` defines one **paid Starter web service** in Singapore with a **1 GB persistent disk**. Review the current estimate in Render before applying. No paid resources are created by committing this file. Render's free web services cannot attach persistent disks and would lose this application's users and records on restart/redeploy, so do not switch this blueprint to Free.

## Create the service

1. Sign into Render and authorize access to this GitHub repository (including private repository access if needed).
2. Open [Deploy to Render](https://dashboard.render.com/select-repo?type=blueprint&repo=https%3A%2F%2Fgithub.com%2FRithvikKuchi%2FACXIOM-DRIVE), or choose **New > Blueprint** and select this repository. Use `main` and `render.yaml`.
3. Enter `Bootstrap__Email` (your administrator email) and `Bootstrap__Password` (a unique password, at least 10 characters with uppercase, lowercase, number and symbol). These values stay in Render; never put them in GitHub. Public development/demo credentials are not created in Production.
4. Review the paid Starter service and 1 GB disk, then apply only after accepting the displayed cost. The Dockerfile builds .NET 10. Database initialization occurs at runtime after the disk is mounted.
5. Wait for **Live** and open the service's HTTPS `onrender.com` URL. `/healthz` must return 200. Sign in with the administrator credentials and create sales users under Users & roles.
6. Create a test record, restart the service, and verify that the record and authenticated session survive. This is the final cloud check; local tests cannot verify Render's actual disk attachment or permissions.
7. Remove `Bootstrap__Password` from the Render service after the administrator exists. Existing accounts remain on the disk. Take SQLite-consistent backups before storing important data.

## Hosting behavior

- The service listens on `PORT` (10000 in this blueprint).
- Render provides `RENDER=true` and `RENDER_EXTERNAL_HOSTNAME`; the hostname is added to allowed hosts. For custom domains, also add the exact hostname to `AllowedHosts`, keeping existing values.
- `Hosting__DataPath=/app/App_Data` must refer to the attached persistent disk. Both `acxiom.db` and the Data Protection key ring live there. Do not remove the disk or mount over `/app`.
- `Hosting__RenderProxy=true` trusts only one forwarded scheme header from Render's managed ingress. It does not trust forwarded client IP or host headers. Keep the private network restricted to trusted services; throttling and audit IPs use the immediate peer.
- HTTPS redirection, Secure/HttpOnly cookies, CSRF protection and role/owner authorization remain enabled. Public registration is disabled.
- One instance is configured because this deployment uses a local SQLite disk. Disk deployments can have brief downtime.
- Successful GitHub checks trigger subsequent deployments. GitHub Actions run the HTTP acceptance suite and production persistence checks for both Railway and Render.
- The temporary PC-hosted demo's database is **not** uploaded by this blueprint. The cloud service starts with an empty database and the configured administrator.

## Local verification

```text
dotnet build -c Release
python tests/hosting_test.py --provider render --output TestResults/render-hosting.json
```

These tests simulate ingress and a mounted directory, verify login and HTTPS behavior, and restart the application to check data and key persistence. A successful result is not proof that a Render deployment exists.

## References

- [Render Blueprint fields](https://render.com/docs/blueprint-spec)
- [Persistent disks and plan requirements](https://render.com/docs/disks)
- [Render-provided environment variables](https://render.com/docs/environment-variables)
- [Health check hostname behavior](https://render.com/docs/health-checks)
- [Current Render pricing](https://render.com/pricing)
