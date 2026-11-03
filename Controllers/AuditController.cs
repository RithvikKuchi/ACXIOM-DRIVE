using AcxiomCRM.Data;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin")]
public class AuditController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string userId = "", string module = "", string actionFilter = "", string query = "", DateTime? from = null, DateTime? to = null, int page = 1)
    {
        var model = new AuditViewModel { UserId = userId, Module = module, ActionFilter = actionFilter, Query = query, From = from, To = to };
        if (from > to) ModelState.AddModelError("", "Start date must be on or before end date.");
        var logs = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(userId)) logs = logs.Where(x => x.UserId == userId);
        if (!string.IsNullOrWhiteSpace(module)) logs = logs.Where(x => x.EntityName == module);
        if (!string.IsNullOrWhiteSpace(actionFilter)) logs = logs.Where(x => x.Action == actionFilter);
        if (!string.IsNullOrWhiteSpace(query)) logs = logs.Where(x => x.Action.Contains(query) || x.EntityName.Contains(query) || (x.RecordId != null && x.RecordId.Contains(query)));
        if (from.HasValue) logs = logs.Where(x => x.CreatedDate >= from.Value.Date);
        if (to.HasValue) { var end = to.Value.Date.AddDays(1); logs = logs.Where(x => x.CreatedDate < end); }
        model.Total = ModelState.IsValid ? await logs.CountAsync() : 0;
        model.Page = Math.Clamp(page, 1, model.Pages);
        if (ModelState.IsValid) model.Events = await logs.OrderByDescending(x => x.CreatedDate).ThenByDescending(x => x.Id).Skip((model.Page - 1) * 20).Take(20).ToListAsync();
        model.Users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Name);
        model.Modules = await db.AuditLogs.Select(x => x.EntityName).Distinct().OrderBy(x => x).ToListAsync();
        model.Actions = await db.AuditLogs.Select(x => x.Action).Distinct().OrderBy(x => x).ToListAsync();
        return View(model);
    }
}
