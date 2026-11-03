# Verification results

**133/133 HTTP acceptance checks passed.**

Execution timestamp (UTC): 2026-11-03T18:57:49.745168+00:00

Target: the self-contained Windows x64 Release publish, running with a fresh disposable SQLite database in Development mode.

Release compilation succeeded with zero warnings and zero errors. The published executable started without relying on the installed SDK.

## Verification boundary

These checks exercise real HTTP requests, database persistence, Razor rendering, authorization and business workflows. They do not execute browser JavaScript or verify visual layout; browser visual QA remains incomplete. No production load, penetration or hosted deployment test is claimed.

## Checks

| Result | Check |
|---|---|
| PASS | Anonymous dashboard redirects to login |
| PASS | Anonymous customer API returns 401 |
| PASS | Anonymous user administration is protected |
| PASS | API login without CSRF is rejected |
| PASS | Invalid login is rejected |
| PASS | Login admin@acxiom.local |
| PASS | Login manager@acxiom.local |
| PASS | Login sales@acxiom.local |
| PASS | Login sales2@acxiom.local |
| PASS | Sales sees only its own assignment option |
| PASS | Admin sees the active sales team |
| PASS | Admin page renders: / |
| PASS | Admin page renders: /Crm/Index?kind=customers |
| PASS | Admin page renders: /Crm/Index?kind=leads |
| PASS | Admin page renders: /Crm/Index?kind=opportunities |
| PASS | Admin page renders: /Crm/Index?kind=followups |
| PASS | Admin page renders: /Crm/Index?kind=activities |
| PASS | Admin page renders: /Users |
| PASS | Admin page renders: /Audit |
| PASS | Create form renders: customers |
| PASS | Create form renders: leads |
| PASS | Create form renders: opportunities |
| PASS | Create form renders: followups |
| PASS | Create form renders: activities |
| PASS | Report renders: customers |
| PASS | Report renders: leads |
| PASS | Report renders: opportunities |
| PASS | Report renders: followups |
| PASS | Report renders: pipeline |
| PASS | Report renders: conversion |
| PASS | Report renders: activity |
| PASS | Report renders: audit |
| PASS | Manager pipeline access |
| PASS | Manager cannot administer users |
| PASS | Sales cannot view audit log |
| PASS | Sales cannot view audit report |
| PASS | Missing required customer fields |
| PASS | Invalid customer email |
| PASS | Invalid customer phone |
| PASS | Overlong customer name |
| PASS | Customer creation without CSRF |
| PASS | Create valid customer |
| PASS | Customer DTO excludes internal credentials |
| PASS | Duplicate normalized customer email |
| PASS | Duplicate customer phone |
| PASS | Sales can read its assigned customer |
| PASS | Other sales cannot read customer by ID |
| PASS | Other sales cannot edit customer |
| PASS | Other sales cannot delete customer |
| PASS | Sales cannot assign to another owner |
| PASS | Sales list contains only assigned records |
| PASS | Manager can read team customer |
| PASS | Update customer |
| PASS | Customer update persisted |
| PASS | Customer Details page renders |
| PASS | Customer Edit page renders |
| PASS | Customer Delete page renders |
| PASS | Search and pagination API |
| PASS | Opportunity rejects Zero amount |
| PASS | Opportunity rejects Negative amount |
| PASS | Opportunity rejects Probability above 100 |
| PASS | Opportunity rejects Probability below zero |
| PASS | Opportunity rejects Past expected close date |
| PASS | Opportunity rejects Nonexistent customer reference |
| PASS | Opportunity rejects Invalid stage |
| PASS | Opportunity rejects Money precision |
| PASS | Sales cannot reference another owner customer |
| PASS | Create opportunity |
| PASS | Pipeline JSON API |
| PASS | Close opportunity as won |
| PASS | Won probability normalized to 100 |
| PASS | Won opportunity has recorded outcome date |
| PASS | Reject past new follow-up |
| PASS | Reject missing follow-up relationship |
| PASS | Create follow-up |
| PASS | Reschedule follow-up |
| PASS | Complete follow-up |
| PASS | Create activity |
| PASS | Complete activity |
| PASS | Reject invalid lead status |
| PASS | Reject negative expected value |
| PASS | Create new lead |
| PASS | Reject conversion before qualification |
| PASS | Reject skipped lead transition |
| PASS | Lead transition to Contacted |
| PASS | Lead transition to Qualified |
| PASS | Convert qualified lead into customer |
| PASS | Converted lead links customer |
| PASS | Reject repeated conversion |
| PASS | Converted lead history cannot be deleted |
| PASS | opportunities Details page renders |
| PASS | opportunities Edit page renders |
| PASS | opportunities Delete page renders |
| PASS | followups Details page renders |
| PASS | followups Edit page renders |
| PASS | followups Delete page renders |
| PASS | activities Details page renders |
| PASS | activities Edit page renders |
| PASS | activities Delete page renders |
| PASS | leads Details page renders |
| PASS | leads Edit page renders |
| PASS | leads Delete page renders |
| PASS | Related customer deletion is blocked |
| PASS | Filtered CSV export |
| PASS | CSV has header and created customer |
| PASS | CSV export honors sales ownership |
| PASS | Reject reversed report date range |
| PASS | Audit page contains create/update actions |
| PASS | Audit output does not contain demo password |
| PASS | Razor encodes stored HTML |
| PASS | CSV formula injection is neutralized |
| PASS | Delete activities |
| PASS | Deleted activities returns 404 |
| PASS | Delete followups |
| PASS | Deleted followups returns 404 |
| PASS | Delete opportunities |
| PASS | Deleted opportunities returns 404 |
| PASS | Delete customers |
| PASS | Deleted customers returns 404 |
| PASS | Registration rejects weak password |
| PASS | Registration succeeds with valid policy |
| PASS | Registered account cannot self-select Admin |
| PASS | New registered sales user has no others records |
| PASS | Logout ends API session |
| PASS | Logged-out API is protected |
| PASS | Five failures lock account even with correct password |
| PASS | User administration excludes password hashes |
| PASS | Session cookie is HttpOnly |
| PASS | Browser security headers set |
| PASS | Offline asset available: /lib/bootstrap.min.css |
| PASS | Offline asset available: /lib/chart.umd.js |
| PASS | Offline asset available: /lib/jquery.validate.min.js |
| PASS | Offline asset available: /lib/jquery.validate.unobtrusive.min.js |
