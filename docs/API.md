# AcxiomCRM REST API

The application serves the API and MVC interface from the same origin. The development URL is `http://localhost:5080`; production deployments require HTTPS. Requests and responses use JSON with camelCase property names. Resource IDs are integers; Identity user IDs are strings.

## Authentication and CSRF

Authentication uses ASP.NET Core Identity session cookies, not bearer tokens. Retain the cookie jar for the entire session.

1. `GET /api/auth/csrf` stores the antiforgery cookie and returns `{ "token": "...", "headerName": "X-CSRF-TOKEN" }`.
2. `POST /api/auth/login` with that cookie, the `X-CSRF-TOKEN` header, and `{ "email": "...", "password": "..." }`. The `email` field accepts an email address or username.
3. After successful login, call `GET /api/auth/csrf` again using the same cookie jar. The new token is bound to the authenticated identity.
4. Send the session cookies on protected requests. Send the current CSRF token on **every POST, PUT and DELETE**, including login, conversion and logout. GET requests do not require the header.
5. `POST /api/auth/logout` terminates the session. Fetch a fresh anonymous CSRF token before another login.

Sessions expire after 30 minutes with sliding expiration. Five failed password attempts lock an account for 15 minutes. Login is also limited to 30 requests per minute per IP. Inactive accounts cannot sign in. Passwords, hashes and authentication cookies are never included in the JSON responses.

| Method | Endpoint | Access and response |
|---|---|---|
| GET | `/api/auth/csrf` | Anonymous or authenticated; `200`, token and header name. |
| POST | `/api/auth/login` | Anonymous allowed; requires CSRF. `200` with `name`, `email`, `role`, `message`; invalid credentials or lockout return `401`. |
| GET | `/api/auth/me` | Authenticated; `200` with `id`, `name`, `email`, `role`. |
| POST | `/api/auth/logout` | Authenticated with CSRF; `200` with a message. |

The login input requires both fields. The identifier is limited to 254 characters and the password to 128. Public registration and user administration are provided through the MVC interface; there are no registration or user-administration API endpoints.

## Resource endpoints

Replace `{kind}` with one of `customers`, `leads`, `opportunities`, `followups` or `activities`.

| Method | Endpoint | Success response |
|---|---|---|
| GET | `/api/{kind}` | `200`, paginated records: `{ items, total, page, pageSize }`. |
| GET | `/api/{kind}/{id}` | `200`, one `RecordDto`. |
| POST | `/api/{kind}` | `201`, created `RecordDto`, with its URL in the `Location` header. |
| PUT | `/api/{kind}/{id}` | `200`, updated `RecordDto`. This is a full replacement of editable fields, not a partial update. |
| DELETE | `/api/{kind}/{id}` | `204`, no response body. Related records can prevent deletion. Audit history is retained. |
| POST | `/api/leads/{id}/convert` | No body needed. `201`, the created customer DTO and its `Location`. |
| GET | `/api/owners` | `200`, an array of `{ id, name }` owner options. |
| GET | `/api/reports/pipeline` | `200`, scoped pipeline summary; see below. |

All resource endpoints require authentication. Admin and Manager can access the single team's CRM records. SalesExecutive can access only records assigned to their user ID. A record outside the caller's scope returns `404`. `/api/owners` returns active sales executives for Admin/Manager, and the current user for SalesExecutive. Owners must be active SalesExecutive users. Sales users cannot assign records to another user; that attempt returns `403`.

## Request and response DTOs

POST and PUT accept a shared `RecordInput` model. Send the fields relevant to the selected resource. Omitted strings default to empty, numbers to zero and optional IDs/dates to null. On PUT, send **all relevant editable fields** that should be retained; omitted fields are not preserved automatically.

| JSON field | Type | Used by / meaning |
|---|---|---|
| `name` | string | Customer, lead or opportunity name. Follow-up/activity name is derived from `subject`. |
| `email`, `phone` | strings | Customer and lead contact information. |
| `companyName` | string | Customer and lead company. |
| `address`, `city`, `state` | strings | Customer address. |
| `status` | string | Resource status; allowed values are listed below. |
| `ownerId` | string | Assigned sales executive ID from `/api/owners`. Sales callers may omit it to use their own ID. |
| `source` | string | Lead or opportunity source. |
| `priority` | string | Lead priority: `Low`, `Medium` or `High`. |
| `expectedValue` | decimal | Lead's expected value. |
| `amount` | decimal | Opportunity amount. |
| `probability` | integer | Opportunity probability, 0 through 100. |
| `stage` | string | Opportunity pipeline stage. |
| `expectedCloseDate` | date string or null | Opportunity expected close date, such as `2026-12-01`. |
| `customerId`, `leadId` | integer or null | Related record IDs. Lead `customerId` is assigned only through conversion. |
| `opportunityId` | integer or null | Optional opportunity relationship for a follow-up or activity. |
| `subject`, `type` | strings | Follow-up/activity subject and type. |
| `followUpDate` | date string or null | Follow-up date. |
| `activityDate` | date string or null | Activity date. |
| `description` | string | Activity description. |
| `notes` | string | Notes; used as remarks on follow-ups. |

Dates accept ISO 8601 values; `yyyy-MM-dd` is recommended for business dates. Business-date validation uses the application's host-local calendar date. Creation, modification and closure timestamps are recorded in UTC.

`RecordDto` includes the shared input fields plus these read-only fields:

| Field | Meaning |
|---|---|
| `id` | Record ID. |
| `code` | Customer/lead code or generated resource display code. |
| `ownerName` | Assigned owner's display name. |
| `createdDate` | Creation timestamp. |
| `modifiedDate` | Last modification timestamp, or null. |
| `closedDate` | Opportunity outcome timestamp, or null. |

The shared response shape includes unused fields with empty, zero or null values. Do not submit read-only fields as an update contract. Password hashes, internal Identity objects and entity navigation properties are excluded.

### Validation and workflows

Strings are trimmed and emails are normalized to lowercase. Status, stage, type and priority values are case-sensitive.

| Resource | Required values and business rules |
|---|---|
| Customers | `name`, valid `email`, `phone`, `status`, valid owner. Email and phone must each be unique among customers. Status: `Active` or `Inactive`. |
| Leads | `name`, valid `email`, `phone`, valid owner, `priority`, `status`. Create with status `New`. `expectedValue` is from 0 to 999999999.99, at most two decimal places. |
| Opportunities | `name`, `customerId`, valid owner, `amount`, `probability`, `stage`, matching `status`, `expectedCloseDate`. Amount must be greater than zero and at most 999999999.99, with at most two decimal places. Active close dates cannot be in the past. |
| Follow-ups | `subject`, `type`, valid owner, `status`, `followUpDate`, and at least one related customer, lead or opportunity. A new follow-up cannot be dated before today; an edited `Planned` follow-up cannot be dated before today either. Status: `Planned`, `Completed`, `Missed`, `Cancelled`. |
| Activities | `subject`, `type`, valid owner, `status`, `activityDate`. Relationships are optional to allow standalone tasks. Status: `Planned`, `Completed`, `Cancelled`. |

Phone numbers must match `^[6-9][0-9]{9}$`: ten Indian mobile digits with no country prefix, spaces or punctuation. Follow-up/activity types are `Call`, `Meeting`, `Email`, `Task`. Related records must exist in the caller's scope **and belong to the selected owner**.

Text limits: name/company/subject 150 characters; email 254; phone 10; address 500; city/state/source 80; notes/description 2000; priority 20; status/stage/type 30; owner ID 450.

Lead transitions, in addition to keeping the current status:

| Current | Permitted next status |
|---|---|
| `New` | `Contacted`, `Unqualified`, `Lost` |
| `Contacted` | `Qualified`, `Unqualified`, `Lost` |
| `Qualified` | `Contacted`, `Unqualified`, `Lost`, or conversion through the conversion endpoint |
| `Unqualified`, `Lost` | `New` |
| `Converted` | Read-only; editing and deletion return `409`. |

Conversion accepts only a qualified, unconverted lead. It atomically creates a customer, links the lead to that customer, marks the lead `Converted` and writes audit entries. It rejects an existing customer with the same email or phone. It does not automatically create an opportunity; create one with the returned customer ID.

Opportunity stages `Qualification`, `Proposal`, `Negotiation` require status `Open`. Stage `Won` requires status `Won`; stage `Lost` requires status `Lost`. On save, Won probability becomes 100 and Lost probability becomes 0, and `closedDate` is set. Moving back to an active stage clears the closure date. All supplied probabilities must still be within 0-100 before this normalization.

## List filters and pagination

Example: `GET /api/leads?status=Contacted&sort=name&page=1&pageSize=25`.

| Query parameter | Behavior |
|---|---|
| `q` | Case-insensitive substring across name, subject, email, phone, company, code, owner name and related customer name. |
| `status`, `ownerId`, `stage`, `type` | Exact-match filters. |
| `customerId`, `leadId` | Related-record filters. |
| `from`, `to` | Inclusive date bounds. Use `yyyy-MM-dd`. Lists reject a reversed range with `400`. |
| `overdue=true` | Planned follow-ups whose due date is before today. Use on `followups`. |
| `sort` | `newest` (default), `name`, `name_desc`, `date`, `status`, `amount`. Amount sorts descending; for leads it uses expected value. Unknown sort values use newest first. |
| `page` | Default 1; values below 1 become 1. A page beyond the result set has an empty `items` array. |
| `pageSize` | Default 10; constrained to 1-100 for the API. |

The date field is creation date for customers/leads, expected close date for opportunities, due date for follow-ups, and activity date for activities. `total` is the count after authorization and filters, before pagination. Filters never broaden the caller's authorized scope.

## Pipeline report

`GET /api/reports/pipeline` returns:

- `totalOpenAmount`, `weightedPipeline`, `openCount`, `wonCount`, `lostCount`.
- `byStage`: `{ stage, count, amount, weightedAmount }` for each pipeline stage. Count and amount include matching outcomes; weighted amount includes only open opportunities.
- `byOwner`: `{ ownerId, ownerName, count, amount, weightedAmount }`, grouped from open opportunities.

Weighted value is `amount * probability / 100`. The report accepts the applicable list filters and returns the complete aggregate rather than a paginated array; `page` and `pageSize` do not paginate the summary. `q` searches opportunity DTO fields here, not the related customer's name. `from` and `to` filter expected close dates. SalesExecutive receives their own pipeline; Admin/Manager receive the team scope.

## Error responses

Application errors use this shape; field keys can use model-property casing:

```json
{
  "error": "Please correct the highlighted fields.",
  "errors": {
    "Probability": ["Probability must be between 0 and 100."]
  }
}
```

| Status | Meaning |
|---|---|
| `400` | Malformed JSON/types, missing or invalid values, business-rule failure, duplicate customer caught by validation, or missing/invalid CSRF token. |
| `401` | Not authenticated, expired/invalid session, invalid credentials or locked-out login. |
| `403` | Role/assignment operation is not permitted. |
| `404` | Resource or record does not exist in the caller's authorized scope. |
| `405` | HTTP method is unsupported. |
| `409` | Conversion conflict, converted-lead mutation, dependent records preventing deletion, or a database constraint conflict. |
| `429` | Login rate limit exceeded. |
| `500` | Unexpected failure; response contains a generic message, not database details or a stack trace. |

## Runnable PowerShell example

Start the application first, then run this in PowerShell. It signs in, creates one demo customer assigned to an allowed sales owner, lists the customer, and signs out. Development seed credentials are `admin@acxiom.local` / `Acxiom@2026!`; the credential prompt keeps the password out of shell history. Change the base URL and credentials when using a different environment.

```powershell
$ErrorActionPreference = 'Stop'
$apiBase = 'http://localhost:5080'
$credential = Get-Credential -UserName 'admin@acxiom.local' -Message 'AcxiomCRM login'

# Keep the antiforgery and authentication cookies in the same session.
$csrf = Invoke-RestMethod -Uri "$apiBase/api/auth/csrf" -SessionVariable crmSession
$headers = @{ 'X-CSRF-TOKEN' = $csrf.token }
$loginBody = @{
    email = $credential.UserName
    password = $credential.GetNetworkCredential().Password
} | ConvertTo-Json
$login = Invoke-RestMethod -Uri "$apiBase/api/auth/login" -Method Post -WebSession $crmSession -Headers $headers -ContentType 'application/json' -Body $loginBody
$loginBody = $null
$login | Select-Object name, email, role

# Refresh after identity changes: the anonymous token must not be reused.
$csrf = Invoke-RestMethod -Uri "$apiBase/api/auth/csrf" -WebSession $crmSession
$headers = @{ 'X-CSRF-TOKEN' = $csrf.token }
$owners = Invoke-RestMethod -Uri "$apiBase/api/owners" -WebSession $crmSession
if ($owners.Count -eq 0) { throw 'Create or activate a SalesExecutive user first.' }

$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$phone = '9' + (Get-Random -Minimum 100000000 -Maximum 1000000000).ToString()
$customerBody = @{
    name = "API demo $suffix"
    email = "demo.$suffix@example.com"
    phone = $phone
    companyName = 'Demo Company'
    status = 'Active'
    ownerId = $owners[0].id
    notes = 'Created through the documented REST API example.'
} | ConvertTo-Json
$customer = Invoke-RestMethod -Uri "$apiBase/api/customers" -Method Post -WebSession $crmSession -Headers $headers -ContentType 'application/json' -Body $customerBody
$customer | Select-Object id, code, name, email, ownerName

$list = Invoke-RestMethod -Uri "$apiBase/api/customers?q=$suffix&page=1&pageSize=10" -WebSession $crmSession
$list.items | Select-Object id, name, email, status
Invoke-RestMethod -Uri "$apiBase/api/auth/logout" -Method Post -WebSession $crmSession -Headers $headers
```

The example intentionally leaves its customer in the database for inspection. Use the UI or `DELETE /api/customers/{id}` while authenticated to remove it. Random contact values avoid collisions on repeated runs; a duplicate phone validation error can be resolved by rerunning with a new phone.
