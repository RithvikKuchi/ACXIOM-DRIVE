# AcxiomCRM

A working ASP.NET Core MVC sales CRM built for the AcxiomCRM project assignment. It includes ASP.NET Core Identity, three roles, customer and lead management, opportunity pipelines, follow-ups, activities, an audit trail, REST APIs, dashboards and CSV reports.

## Run the project

Install the **.NET 10 SDK**, open a terminal in this folder, and run:

```powershell
dotnet restore
dotnet run --project AcxiomCRM.csproj
```

Open **http://localhost:5080**. On Windows, you can instead double-click `RUN.cmd`. The launch profile sets Development mode and creates a local SQLite database plus demonstration records automatically. No SQL Server, Node.js or internet connection is needed after dependencies have been restored. Bootstrap, Chart.js and validation libraries are bundled locally.

The separately supplied **Windows demo ZIP** includes the .NET runtime: extract it and run `START_ACXIOM.cmd`. It does not require an SDK installation. Keep the console open while using the app; press Ctrl+C to stop it.

## Demo sign-ins

All four demo accounts use the password **`Acxiom@2026!`**. These are public demonstration credentials, seeded only in Development mode.

| Role | Email | Access |
|---|---|---|
| Admin | admin@acxiom.local | All CRM records, reports, users and audit logs |
| Manager | manager@acxiom.local | Entire sales team CRM and management reports |
| Sales Executive | sales@acxiom.local | Rohan's assigned records and scoped reports |
| Sales Executive | sales2@acxiom.local | Ananya's assigned records and scoped reports |

New registration always assigns `SalesExecutive`; a submitted role cannot grant administrator access. This implementation uses one sales team, so Managers see the whole team's business records.

## Features

- Customers: create, edit, details/history, delete with relationship protection, unique email and mobile number, owner assignment and filtering.
- Leads: capture, search, status transitions and atomic qualified-lead conversion into a customer. Create an opportunity from the converted customer's details.
- Opportunities: stage, amount, probability, expected close date, won/lost outcome date and weighted pipeline.
- Follow-ups and activities: schedule calls, meetings, emails and tasks; reschedule and complete; view overdue/upcoming work.
- Dashboard: all required KPI cards and Chart.js lead-status, opportunity-stage and monthly-outcome charts, with authorized data and date filters.
- Reports: customer, lead, opportunity, follow-up, pipeline, conversion, user activity and audit. Filtering, sorting, pagination and CSV export.
- Administration: user creation, role assignment, activation/deactivation, password reset, unlock and last-active-admin protection.
- Security: Identity password hashing, 10-character password policy, 5-failure/15-minute lockout, cookie sessions, rate limiting, anti-forgery checks, server ownership checks and append-only audit records.
- REST: secured CRUD APIs for all five CRM resources, lead conversion, owner lookup, scoped pipeline and cookie-authentication endpoints.

## Documentation

- [Project report and implementation details](docs/PROJECT_REPORT.md)
- [Evaluator demonstration guide](docs/DEMO_GUIDE.md)
- [REST API reference and executable example](docs/API.md)
- [Verification results](docs/TEST_RESULTS.md)
- [Third-party libraries and licenses](wwwroot/lib/THIRD_PARTY_NOTICES.md)

## Structure

```text
Controllers/       MVC and API request handlers
Models/            Domain entities and safe request/response DTOs
Data/              IdentityDbContext, relationships, seed and audit triggers
Services/          Shared business rules, scope checks and auditing
ViewModels/        Page-specific models and filters
Views/             Razor pages and shared layout
wwwroot/           Local CSS, JavaScript and third-party libraries
tests/             Repeatable HTTP acceptance tests and result JSON
docs/              Report, API reference, demonstration and test evidence
```

SQLite creates `App_Data/acxiom.db` on first launch. This file is intentionally excluded from Git and delivery packages. The model uses separate CRM tables with shared fields in `CrmRecords` (EF table-per-type mapping), Identity tables, foreign keys, indexes, unique customer identifiers, and triggers that reject audit modification/deletion.

## Run acceptance tests

Use a fresh disposable database: the test suite creates synthetic records and tests lockout. In a first PowerShell window:

```powershell
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ConnectionStrings__DefaultConnection='Data Source=App_Data/acceptance.db'
dotnet run --no-launch-profile --urls http://localhost:5081
```

In a second window, with Python 3 installed:

```powershell
python -m pip install -r tests/requirements.txt
python tests/smoke_test.py --base http://localhost:5081 --output tests/results.json
```

The delivered test report records actual HTTP checks. Razor pages are compiled and requested by the suite. Browser visual and interactive JavaScript verification are separate from those HTTP checks; see the report for the verification boundary.

## Deployment notes

This delivery is an assignment/demo application with production-oriented controls, not a hosted production deployment. To configure a deployment:

1. Run in `Production` with a fresh database; Development demo accounts are not seeded there.
2. Configure HTTPS and a real certificate; production cookies require HTTPS. Configure your actual hostname in `AllowedHosts`. A reverse proxy needs explicitly trusted forwarded-header configuration for your hosting topology.
3. Set the first admin through secret environment variables `Bootstrap__Email`, `Bootstrap__Password`, and optional `Bootstrap__Name`. Bootstrap runs only when no Admin exists. Remove these variables after initialization. Never commit credentials.
4. Set `Authentication__AllowRegistration=false` if administrators should create all accounts.
5. Persist and back up the SQLite data directory and ASP.NET Data Protection keys. Keep access restricted to the application account.
6. This demo initializes its schema with `EnsureCreated`; introduce reviewed EF migrations before changing a deployed schema. It is designed for a small, single-instance sales team. It does not include email delivery, MFA, multi-tenant teams or a production load/security audit.

Amounts are INR. Customer/lead mobile numbers use the documented Indian format: 10 digits, starting with 6, 7, 8 or 9. Business dates use the host's local date; audit/outcome timestamps are UTC. Configure the host timezone for the sales team.

## Framework references

- [ASP.NET Core role authorization](https://learn.microsoft.com/en-us/aspnet/core/mvc/security/authorization/roles?view=aspnetcore-10.0)
- [ASP.NET Core anti-forgery protection](https://learn.microsoft.com/en-us/aspnet/core/security/anti-request-forgery?view=aspnetcore-10.0)

