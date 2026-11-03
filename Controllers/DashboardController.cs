using AcxiomCRM.Data;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AcxiomCRM.Controllers;

[Authorize]
public class DashboardController(CrmService crm, ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string period = "all", DateTime? from = null, DateTime? to = null)
    {
        var today = DateTime.Today;
        switch (period)
        {
            case "today": from = today; to = today; break;
            case "week": from = today.AddDays(-(((int)today.DayOfWeek + 6) % 7)); to = from.Value.AddDays(6); break;
            case "month": from = new DateTime(today.Year, today.Month, 1); to = from.Value.AddMonths(1).AddDays(-1); break;
            case "custom": break;
            default: period = "all"; from = null; to = null; break;
        }
        if (from > to) ModelState.AddModelError("", "Start date must be on or before end date.");
        bool InRange(DateTime date) => (!from.HasValue || date.Date >= from.Value.Date) && (!to.HasValue || date.Date <= to.Value.Date);
        var customers = await crm.ListAsync("customers", User);
        var leads = await crm.ListAsync("leads", User);
        var opportunities = await crm.ListAsync("opportunities", User);
        var followups = await crm.ListAsync("followups", User);
        customers = customers.Where(x => InRange(x.CreatedDate)).ToList();
        leads = leads.Where(x => InRange(x.CreatedDate)).ToList();
        opportunities = opportunities.Where(x => InRange(x.CreatedDate)).ToList();
        var open = opportunities.Where(x => x.Stage != "Won" && x.Stage != "Lost").ToList();
        var pending = followups.Where(x => x.Status == "Planned").ToList();
        string[] leadLabels = ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"];
        string[] stages = ["Qualification", "Proposal", "Negotiation", "Won", "Lost"];
        var endMonth = new DateTime((to ?? today).Year, (to ?? today).Month, 1);
        var months = Enumerable.Range(0, 6).Select(i => endMonth.AddMonths(i - 5)).ToArray();
        // Monthly revenue is grouped by the final-outcome timestamp, not the forecast close date.
        var chart = new
        {
            leads = new { labels = leadLabels, values = leadLabels.Select(s => leads.Count(x => x.Status == s)) },
            pipeline = new { labels = stages, values = stages.Select(s => opportunities.Where(x => x.Stage == s).Sum(x => x.Amount)) },
            monthly = new
            {
                labels = months.Select(m => m.ToString("MMM yy")),
                won = months.Select(m => opportunities.Where(x => x.Stage == "Won" && OutcomeDate(x).Year == m.Year && OutcomeDate(x).Month == m.Month).Sum(x => x.Amount)),
                lost = months.Select(m => opportunities.Where(x => x.Stage == "Lost" && OutcomeDate(x).Year == m.Year && OutcomeDate(x).Month == m.Month).Sum(x => x.Amount))
            }
        };
        var model = new DashboardViewModel
        {
            Period = period, From = from, To = to,
            Scope = User.IsInRole("SalesExecutive") ? "Your assigned records" : "Entire sales team",
            TotalCustomers = customers.Count, TotalLeads = leads.Count,
            OpenLeads = leads.Count(x => x.Status is "New" or "Contacted" or "Qualified"),
            TotalOpportunities = opportunities.Count, OpenOpportunities = open.Count,
            WonOpportunities = opportunities.Count(x => x.Stage == "Won"), LostOpportunities = opportunities.Count(x => x.Stage == "Lost"),
            Pipeline = open.Sum(x => x.Amount), WeightedPipeline = open.Sum(x => x.Amount * x.Probability / 100m),
            PendingFollowUps = pending.Count, OverdueFollowUps = pending.Count(x => x.FollowUpDate?.Date < today),
            Upcoming = pending.OrderBy(x => x.FollowUpDate).Take(6).ToList(),
            RecentLeads = leads.OrderByDescending(x => x.CreatedDate).Take(5).ToList(),
            ChartJson = JsonSerializer.Serialize(chart)
        };
        if (User.IsInRole("Admin"))
        {
            model.ActiveUsers = await db.Users.CountAsync(x => x.IsActive);
            var utcToday = DateTime.UtcNow.Date;
            model.SecurityEventsToday = await db.AuditLogs.CountAsync(x => x.CreatedDate >= utcToday && (x.EntityName == "Authentication" || x.EntityName == "Users"));
        }
        return View(model);
    }

    private static DateTime OutcomeDate(AcxiomCRM.Models.RecordDto record)
    {
        return record.ClosedDate ?? record.ModifiedDate ?? record.CreatedDate;
    }
}
