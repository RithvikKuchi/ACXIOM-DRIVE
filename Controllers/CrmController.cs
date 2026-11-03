using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AcxiomCRM.Controllers;

[Authorize]
public class CrmController(CrmService crm, ApplicationDbContext db) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(string kind = "customers", [FromQuery] CrmFilter? filter = null)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        filter ??= new();
        filter.PageSize = Math.Clamp(filter.PageSize, 5, 100);
        var customers = await crm.ListAsync("customers", User);
        var rows = filter.Apply(await crm.ListAsync(kind, User), kind, customers.ToDictionary(r => r.Id, CrmUi.Label)).ToList();
        filter.Page = Math.Clamp(filter.Page, 1, Math.Max(1, (int)Math.Ceiling(rows.Count / (double)filter.PageSize)));
        return View(new CrmListModel { Kind = kind, Filter = filter, Items = rows.Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize).ToList(), Total = rows.Count, Owners = await crm.OwnersAsync(User), Customers = customers, Leads = await crm.ListAsync("leads", User) });
    }

    [HttpGet]
    public async Task<IActionResult> Create(string kind = "customers", int? customerId = null, int? leadId = null, int? opportunityId = null)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        var input = new RecordInput
        {
            Status = CrmUi.Statuses(kind)[0], Stage = "Qualification", Priority = "Medium", Type = "Call",
            OwnerId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "", CustomerId = customerId, LeadId = leadId,
            OpportunityId = opportunityId, ExpectedCloseDate = DateTime.Today.AddDays(30), FollowUpDate = DateTime.Today.AddDays(1), ActivityDate = DateTime.Today, Probability = 25
        };
        return View("Form", await FormModel(kind, null, input));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string kind, [Bind(Prefix = "Input")] RecordInput input)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                var saved = await crm.SaveAsync(kind, null, input, User);
                TempData["Success"] = $"{CrmUi.Singular(kind)} created successfully.";
                return RedirectToAction(nameof(Details), new { kind, id = saved.Id });
            }
            catch (CrmException ex) { if (ex.StatusCode is 403 or 404) return StatusCode(ex.StatusCode); AddErrors(ex); }
        }
        return View("Form", await FormModel(kind, null, input));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        try
        {
            var row = await crm.GetAsync(kind, id, User);
            return View("Form", await FormModel(kind, id, row, row.Status));
        }
        catch (CrmException ex) { return StatusCode(ex.StatusCode); }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string kind, int id, [Bind(Prefix = "Input")] RecordInput input)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        RecordDto original;
        try { original = await crm.GetAsync(kind, id, User); }
        catch (CrmException ex) { return StatusCode(ex.StatusCode); }
        if (ModelState.IsValid)
        {
            try
            {
                await crm.SaveAsync(kind, id, input, User);
                TempData["Success"] = $"{CrmUi.Singular(kind)} updated successfully.";
                return RedirectToAction(nameof(Details), new { kind, id });
            }
            catch (CrmException ex) { if (ex.StatusCode is 403 or 404) return StatusCode(ex.StatusCode); AddErrors(ex); }
        }
        return View("Form", await FormModel(kind, id, input, original.Status));
    }

    [HttpGet]
    public async Task<IActionResult> Details(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        try { return View(await DetailsModel(kind, id)); }
        catch (CrmException ex) { return StatusCode(ex.StatusCode); }
    }

    [HttpGet]
    public async Task<IActionResult> Delete(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        try { return View(await DetailsModel(kind, id)); }
        catch (CrmException ex) { return StatusCode(ex.StatusCode); }
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return NotFound();
        try
        {
            await crm.DeleteAsync(kind, id, User);
            TempData["Success"] = $"{CrmUi.Singular(kind)} deleted successfully.";
            return RedirectToAction(nameof(Index), new { kind });
        }
        catch (CrmException ex)
        {
            if (ex.StatusCode is 403 or 404) return StatusCode(ex.StatusCode);
            ModelState.AddModelError("", ex.Message);
            return View("Delete", await DetailsModel(kind, id));
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id)
    {
        try
        {
            var customer = await crm.ConvertLeadAsync(id, User);
            TempData["Success"] = "Lead converted. Your customer is ready; create an opportunity to continue the sales journey.";
            return RedirectToAction(nameof(Details), new { kind = "customers", id = customer.Id });
        }
        catch (CrmException ex)
        {
            if (ex.StatusCode is 403 or 404) return StatusCode(ex.StatusCode);
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Details), new { kind = "leads", id });
        }
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(string kind, int id)
    {
        if (kind is not ("followups" or "activities")) return NotFound();
        try
        {
            var record = await crm.GetAsync(kind, id, User);
            record.Status = "Completed";
            await crm.SaveAsync(kind, id, record, User);
            TempData["Success"] = "Activity marked as completed.";
        }
        catch (CrmException ex)
        {
            if (ex.StatusCode is 403 or 404) return StatusCode(ex.StatusCode);
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Details), new { kind, id });
    }

    private async Task<CrmFormModel> FormModel(string kind, int? id, RecordInput input, string originalStatus = "")
    {
        var model = new CrmFormModel { Kind = kind, Id = id, Input = input, OriginalStatus = originalStatus, Owners = await crm.OwnersAsync(User) };
        model.Customers = await crm.ListAsync("customers", User);
        model.Leads = await crm.ListAsync("leads", User);
        model.Opportunities = await crm.ListAsync("opportunities", User);
        if (!id.HasValue && model.Owners.All(o => o.Id != model.Input.OwnerId)) model.Input.OwnerId = model.Owners.FirstOrDefault()?.Id ?? "";
        return model;
    }

    private async Task<CrmDetailsModel> DetailsModel(string kind, int id)
    {
        var row = await crm.GetAsync(kind, id, User); // Authorize before querying record history.
        var entity = CrmUi.Entity(kind);
        var recordId = id.ToString();
        var history = await db.AuditLogs.AsNoTracking().Where(a => a.RecordId == recordId && (a.EntityName == entity || a.EntityName == kind)).OrderByDescending(a => a.CreatedDate).Take(30).Select(a => new RecordEvent(a.Action, a.CreatedDate, a.Result)).ToListAsync();
        var model = new CrmDetailsModel { Kind = kind, Item = row, History = history };
        if (kind is "customers" or "leads" or "opportunities")
        {
            model.FollowUps = (await crm.ListAsync("followups", User)).Where(f => kind == "customers" ? f.CustomerId == id : kind == "leads" ? f.LeadId == id : f.OpportunityId == id).OrderBy(f => f.FollowUpDate).ToList();
            model.Activities = (await crm.ListAsync("activities", User)).Where(f => kind == "customers" ? f.CustomerId == id : kind == "leads" ? f.LeadId == id : f.OpportunityId == id).OrderByDescending(f => f.ActivityDate).ToList();
        }
        model.CustomerNames = (await crm.ListAsync("customers", User)).ToDictionary(c => c.Id, CrmUi.Label);
        model.LeadNames = (await crm.ListAsync("leads", User)).ToDictionary(c => c.Id, CrmUi.Label);
        return model;
    }

    private void AddErrors(CrmException ex)
    {
        if (ex.Errors.Count == 0) ModelState.AddModelError("", ex.Message);
        foreach (var (key, errors) in ex.Errors)
            foreach (var error in errors) ModelState.AddModelError(string.IsNullOrWhiteSpace(key) ? "" : $"Input.{key}", error);
    }
}
