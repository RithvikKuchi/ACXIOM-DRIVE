using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace AcxiomCRM.Controllers;

[Authorize]
public class ReportsController(CrmService crm, ApplicationDbContext db) : Controller
{
    private static readonly string[] Types = ["customers", "leads", "opportunities", "followups", "pipeline", "conversion", "activity", "audit"];

    public async Task<IActionResult> Index(string type = "pipeline", string query = "", string status = "", string ownerId = "", DateTime? from = null, DateTime? to = null, int sort = 0, bool desc = false, int page = 1, bool export = false)
    {
        if (!Types.Contains(type)) return NotFound();
        if (type == "audit" && !User.IsInRole("Admin")) return Forbid();
        if (type == "activity" && !User.IsInRole("Admin") && !User.IsInRole("Manager")) return Forbid();
        if (from > to) ModelState.AddModelError("", "Start date must be on or before end date.");
        var model = new ReportViewModel { Type = type, Query = query, Status = status, OwnerId = ownerId, From = from, To = to, Sort = sort, Desc = desc, Owners = await crm.OwnersAsync(User) };
        bool DateMatches(DateTime date) => (!from.HasValue || date.Date >= from.Value.Date) && (!to.HasValue || date.Date <= to.Value.Date);
        List<RecordDto> Filter(IEnumerable<RecordDto> records, Func<RecordDto, DateTime>? dateSelector = null)
        {
            dateSelector ??= x => x.CreatedDate;
            return records.Where(x => (string.IsNullOrEmpty(ownerId) || x.OwnerId == ownerId) && (string.IsNullOrEmpty(status) || x.Status == status || x.Stage == status) && DateMatches(dateSelector(x)) && (string.IsNullOrWhiteSpace(query) || string.Join(" ", x.Name, x.Email, x.Phone, x.CompanyName, x.Source, x.Subject, x.OwnerName).Contains(query, StringComparison.OrdinalIgnoreCase))).ToList();
        }
        List<ReportRow> rows = [];
        switch (type)
        {
            case "customers":
            {
                model.Title = "Customer report"; model.Description = "Customer directory and ownership. Date filters use creation date.";
                model.Columns = ["Code", "Customer", "Company", "Email", "Phone", "Status", "Owner", "Created"];
                var all = await crm.ListAsync("customers", User); model.Statuses = all.Select(x => x.Status).Distinct().Order().ToList();
                rows = Filter(all).Select(x => new ReportRow([x.Code, x.Name, x.CompanyName, x.Email, x.Phone, x.Status, x.OwnerName, Date(x.CreatedDate)])).ToList();
                break;
            }
            case "leads":
            {
                model.Title = "Lead report"; model.Description = "Lead sources, qualification and conversion. Date filters use creation date.";
                model.Columns = ["Code", "Lead", "Source", "Status", "Priority", "Expected value", "Owner", "Converted customer", "Created"];
                var all = await crm.ListAsync("leads", User); model.Statuses = all.Select(x => x.Status).Distinct().Order().ToList();
                rows = Filter(all).Select(x => new ReportRow([x.Code, x.Name, x.Source, x.Status, x.Priority, Number(x.ExpectedValue), x.OwnerName, x.CustomerId?.ToString() ?? "", Date(x.CreatedDate)])).ToList();
                break;
            }
            case "opportunities":
            {
                model.Title = "Opportunity report"; model.Description = "Opportunity values, stage and forecast dates. Date filters use expected close date.";
                model.Columns = ["Opportunity", "Stage", "Status", "Amount", "Probability %", "Weighted amount", "Expected close", "Owner"];
                var all = await crm.ListAsync("opportunities", User); model.Statuses = all.Select(x => x.Stage).Distinct().Order().ToList();
                rows = Filter(all, x => x.ExpectedCloseDate ?? x.CreatedDate).Select(x => new ReportRow([x.Name, x.Stage, x.Status, Number(x.Amount), x.Probability.ToString(), Number(x.Amount * x.Probability / 100m), Date(x.ExpectedCloseDate), x.OwnerName])).ToList();
                break;
            }
            case "followups":
            {
                model.Title = "Follow-up report"; model.Description = "Scheduled, completed and overdue follow-ups. Date filters use follow-up date.";
                model.Columns = ["Subject", "Type", "Date", "Status", "Overdue", "Owner", "Customer ID", "Lead ID", "Opportunity ID"];
                var all = await crm.ListAsync("followups", User); model.Statuses = all.Select(x => x.Status).Distinct().Order().ToList();
                rows = Filter(all, x => x.FollowUpDate ?? x.CreatedDate).Select(x => new ReportRow([x.Subject, x.Type, Date(x.FollowUpDate), x.Status, x.Status == "Planned" && x.FollowUpDate?.Date < DateTime.Today ? "Yes" : "No", x.OwnerName, x.CustomerId?.ToString() ?? "", x.LeadId?.ToString() ?? "", x.OpportunityId?.ToString() ?? ""])).ToList();
                break;
            }
            case "pipeline":
            {
                model.Title = "Sales pipeline"; model.Description = "Open opportunities grouped by stage and owner. Date filters use expected close date. All amounts in INR.";
                model.Columns = ["Stage", "Owner", "Opportunities", "Pipeline amount", "Weighted pipeline"];
                var all = await crm.ListAsync("opportunities", User);
                model.Statuses = ["Qualification", "Proposal", "Negotiation"];
                var filtered = Filter(all.Where(x => x.Stage != "Won" && x.Stage != "Lost"), x => x.ExpectedCloseDate ?? x.CreatedDate);
                rows = filtered.GroupBy(x => new { x.Stage, x.OwnerId, x.OwnerName }).Select(g => new ReportRow([g.Key.Stage, g.Key.OwnerName, g.Count().ToString(), Number(g.Sum(x => x.Amount)), Number(g.Sum(x => x.Amount * x.Probability / 100m))])).ToList();
                model.Metrics = new() { ["Open opportunities"] = filtered.Count.ToString(), ["Pipeline value"] = "INR " + Number(filtered.Sum(x => x.Amount)), ["Weighted pipeline"] = "INR " + Number(filtered.Sum(x => x.Amount * x.Probability / 100m)) };
                break;
            }
            case "conversion":
            {
                model.Title = "Sales & conversion"; model.Description = "Conversion by owner and opportunity outcomes. Date filters select records by creation date.";
                model.Columns = ["Owner", "Leads", "Converted", "Conversion %", "Open opportunities", "Won", "Lost", "Won revenue"];
                var leads = Filter(await crm.ListAsync("leads", User)); var opportunities = Filter(await crm.ListAsync("opportunities", User));
                var owners = leads.Select(x => (x.OwnerId, x.OwnerName)).Concat(opportunities.Select(x => (x.OwnerId, x.OwnerName))).Distinct();
                rows = owners.Select(o =>
                {
                    var l = leads.Where(x => x.OwnerId == o.OwnerId).ToList(); var p = opportunities.Where(x => x.OwnerId == o.OwnerId).ToList();
                    int converted = l.Count(x => x.Status == "Converted");
                    return new ReportRow([o.OwnerName, l.Count.ToString(), converted.ToString(), Number(l.Count == 0 ? 0 : 100m * converted / l.Count), p.Count(x => x.Stage != "Won" && x.Stage != "Lost").ToString(), p.Count(x => x.Stage == "Won").ToString(), p.Count(x => x.Stage == "Lost").ToString(), Number(p.Where(x => x.Stage == "Won").Sum(x => x.Amount))]);
                }).ToList();
                model.Metrics = new() { ["Total leads"] = leads.Count.ToString(), ["Converted leads"] = leads.Count(x => x.Status == "Converted").ToString(), ["Won revenue"] = "INR " + Number(opportunities.Where(x => x.Stage == "Won").Sum(x => x.Amount)) };
                break;
            }
            case "activity":
            {
                model.Title = "User activity"; model.Description = "CRM action counts by user. Managers see business actions only; administrators see all audited actions. Date filters use event date.";
                model.Columns = ["User", "Action", "Module", "Count"];
                var logs = db.AuditLogs.AsNoTracking().AsQueryable();
                if (!User.IsInRole("Admin")) logs = logs.Where(x => x.EntityName == "Customer" || x.EntityName == "Lead" || x.EntityName == "Opportunity" || x.EntityName == "FollowUp" || x.EntityName == "Activity" || x.EntityName == "customers" || x.EntityName == "leads" || x.EntityName == "opportunities" || x.EntityName == "followups" || x.EntityName == "activities");
                if (!string.IsNullOrEmpty(ownerId)) logs = logs.Where(x => x.UserId == ownerId);
                if (from.HasValue) logs = logs.Where(x => x.CreatedDate >= from.Value.Date);
                if (to.HasValue) { var end = to.Value.Date.AddDays(1); logs = logs.Where(x => x.CreatedDate < end); }
                if (!string.IsNullOrEmpty(status)) logs = logs.Where(x => x.Action == status);
                var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
                var events = await logs.ToListAsync();
                model.Statuses = events.Select(x => x.Action).Distinct().Order().ToList();
                rows = events.GroupBy(x => new { x.UserId, x.Action, x.EntityName }).Select(g => new ReportRow([g.Key.UserId != null && users.TryGetValue(g.Key.UserId, out var name) ? name : "System / anonymous", g.Key.Action, g.Key.EntityName, g.Count().ToString()])).Where(r => string.IsNullOrWhiteSpace(query) || string.Join(" ", r.Cells).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                break;
            }
            case "audit":
            {
                model.Title = "Audit report"; model.Description = "Security and business events. Timestamps and date filters are UTC. Detailed before/after values are available in Audit Log.";
                model.Columns = ["Timestamp UTC", "User", "Action", "Module", "Record ID", "Result", "IP address"];
                var logs = db.AuditLogs.AsNoTracking().AsQueryable();
                if (!string.IsNullOrEmpty(ownerId)) logs = logs.Where(x => x.UserId == ownerId);
                if (!string.IsNullOrEmpty(status)) logs = logs.Where(x => x.Action == status);
                if (from.HasValue) logs = logs.Where(x => x.CreatedDate >= from.Value.Date);
                if (to.HasValue) { var end = to.Value.Date.AddDays(1); logs = logs.Where(x => x.CreatedDate < end); }
                var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
                var events = await logs.ToListAsync(); model.Statuses = events.Select(x => x.Action).Distinct().Order().ToList();
                rows = events.Select(x => new ReportRow([x.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"), x.UserId != null && users.TryGetValue(x.UserId, out var name) ? name : "System / anonymous", x.Action, x.EntityName, x.RecordId ?? "", x.Result, x.IpAddress ?? ""])).Where(r => string.IsNullOrWhiteSpace(query) || string.Join(" ", r.Cells).Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                break;
            }
        }
        if (!ModelState.IsValid) rows = [];
        model.Sort = Math.Clamp(sort, 0, Math.Max(0, model.Columns.Count - 1));
        var ordered = desc ? rows.OrderByDescending(r => r.Cells[model.Sort], CellComparer.Instance) : rows.OrderBy(r => r.Cells[model.Sort], CellComparer.Instance);
        var orderedRows = ordered.ToList(); model.Total = orderedRows.Count;
        if (export)
        {
            if (!ModelState.IsValid) return BadRequest(new { error = "Invalid report filters." });
            var csv = new StringBuilder(); csv.AppendLine(string.Join(",", model.Columns.Select(CsvCell)));
            foreach (var row in orderedRows) csv.AppendLine(string.Join(",", row.Cells.Select(CsvCell)));
            return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(), "text/csv; charset=utf-8", $"AcxiomCRM-{type}-{DateTime.Today:yyyyMMdd}.csv");
        }
        model.Page = Math.Clamp(page, 1, model.Pages); model.Rows = orderedRows.Skip((model.Page - 1) * model.PageSize).Take(model.PageSize).ToList();
        return View(model);
    }

    private static string Date(DateTime? date) => date?.ToString("yyyy-MM-dd") ?? "";
    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string CsvCell(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && "=+-@\t\r\n".Contains(trimmed[0])) value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
    private sealed class CellComparer : IComparer<string>
    {
        public static CellComparer Instance { get; } = new();
        public int Compare(string? x, string? y)
        {
            if (decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out var a) && decimal.TryParse(y, NumberStyles.Number, CultureInfo.InvariantCulture, out var b)) return a.CompareTo(b);
            return StringComparer.OrdinalIgnoreCase.Compare(x, y);
        }
    }
}
