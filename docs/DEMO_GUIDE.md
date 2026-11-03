# AcxiomCRM evaluator demo guide

## Start the application

Install the **.NET 10 SDK**, then open a terminal in the repository root:

```powershell
dotnet restore
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project AcxiomCRM.csproj --no-launch-profile --urls http://localhost:5080
```

On Windows, `RUN.cmd` performs the development launch. Open **http://localhost:5080** after the server starts. The first run creates `App_Data/acxiom.db`, Identity roles, demonstration users and sample CRM data. Browser libraries are included in the repository; NuGet restore requires internet access on a clean machine.

| Role | Email | Development demo password |
| --- | --- | --- |
| Admin | `admin@acxiom.local` | `Acxiom@2026!` |
| Manager | `manager@acxiom.local` | `Acxiom@2026!` |
| Sales Executive | `sales@acxiom.local` | `Acxiom@2026!` |
| Second Sales Executive | `sales2@acxiom.local` | `Acxiom@2026!` |

These are public demonstration credentials, configured only for Development. Sign out using the account menu when switching users. For simultaneous role comparisons, use separate private browser profiles so cookies remain separate.

## Five-minute walkthrough

| Time | Action | What to demonstrate |
| --- | --- | --- |
| 0:00–0:40 | Open `/` before signing in, then sign in as Admin. | Anonymous users are redirected to Login. Dashboard shows eight KPI cards, three chart areas and follow-up reminders. |
| 0:40–1:30 | Open **Customers → Create**. Try an invalid email/short phone, then correct and save. | Inline validation; persisted customer with generated code, owner and history. Use a unique email and a 10-digit phone starting with 6–9. |
| 1:30–2:15 | Open **Leads**, choose an existing Qualified lead such as Nikhil Bose, then **Convert to customer**. | The lead becomes Converted, links a customer and remains read-only. Open the customer link. |
| 2:15–3:00 | Create an **Opportunity** for that customer with the same owner, amount `100000`, probability `50`, a future close date and stage Qualification/status Open. | Weighted amount is `50000`; zero amount, probability over 100 and past active close date are rejected. |
| 3:00–3:35 | Open **Follow-ups**, choose a Planned record, reschedule or **Mark completed**. | Pending work and lifecycle changes. New/planned past-date entries are rejected. |
| 3:35–4:10 | Open **Reports → Sales pipeline**, filter, sort and export CSV; then open **Audit log**. | Report and export share authorized scope. Audit shows create/update/convert/completion-related events and before/after data. |
| 4:10–5:00 | Switch to Sales Executive, then Manager. | Sales sees only assigned records and no user/audit administration. Manager sees the team pipeline/reports but cannot administer users. |

If a seeded Qualified lead was already converted in a prior demonstration, create a new lead and edit its status in order: **New → Contacted → Qualified**, then convert. The service deliberately rejects a direct New → Qualified jump.

## Useful supporting demonstrations

- **User management:** as Admin, open Users & roles. Create/edit a user with exactly one role, inspect active/lockout state, reset a password or unlock an account. The final active Admin cannot be demoted/deactivated.
- **Activities:** create a Call, Meeting, Email or Task, then edit or mark it completed. Search/filter by type, date, status and owner.
- **API read:** while signed in, open `/api/customers` or `/api/reports/pipeline`. The response is authorized JSON. Opening it after signing out returns 401.
- **API writes:** use `/api/auth/csrf`, retain cookies and send its token as `X-CSRF-TOKEN`. Login requires a token too; obtain a fresh token after login. API updates use PUT with the complete resource input.
- **Account policy:** Register creates a SalesExecutive account. It does not accept a privileged role. Five failed sign-ins lock an account for 15 minutes; demonstrate this with a disposable account, not the Admin needed for the walkthrough.
- **Delete behavior:** delete an unreferenced test record. A linked customer cannot be deleted while dependent records exist; converted leads are retained as conversion history.

## Repeat the acceptance tests

The recorded run passed **133/133 HTTP acceptance checks**. Run the suite only against a disposable Development database: it creates/updates/deletes synthetic data, creates an account and deliberately tests lockout.

Terminal 1, from the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Data Source=App_Data/acceptance-demo.db"
dotnet run --project AcxiomCRM.csproj --no-launch-profile --urls http://localhost:5081
```

Terminal 2:

```powershell
python -m pip install requests
python tests/smoke_test.py --base http://localhost:5081 --output tests/results.json
```

Use an unused database filename for a fresh test run. Stop the server with Ctrl+C when finished. The connection-string environment override applies to that terminal; remove it before returning to the ordinary demo database:

```powershell
Remove-Item Env:ConnectionStrings__DefaultConnection
```

## Verification status

The project compiled and the HTTP acceptance checks passed. **Browser visual QA is not finished.** During the walkthrough, manually verify chart rendering, desktop/tablet layout, keyboard navigation and browser-side field validation. The HTTP tests verify server behavior and page responses; they do not replace those browser checks.

For design decisions, data relationships, exact validation rules and implementation limits, see [Project report](PROJECT_REPORT.md).
