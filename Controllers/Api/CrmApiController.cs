using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AcxiomCRM.Controllers.Api;

/// <summary>Cookie-authenticated CRM API. Send X-CSRF-TOKEN for every write.</summary>
[ApiController]
[Authorize]
[Route("api/{kind}")]
public class CrmApiController(CrmService crm) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(string kind, [FromQuery] CrmFilter filter)
    {
        if (!CrmUi.IsKind(kind)) return UnknownResource();
        if (!ModelState.IsValid) return ValidationProblem(ModelState);
        if (filter.From.HasValue && filter.To.HasValue && filter.From > filter.To)
            return BadRequest(new { error = "From date cannot be after the To date.", errors = new Dictionary<string,string[]> { ["from"] = ["Choose a valid date range."] } });
        var customers = await crm.ListAsync("customers", User);
        var rows = filter.Apply(await crm.ListAsync(kind, User), kind, customers.ToDictionary(c => c.Id, CrmUi.Label)).ToList();
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        return Ok(new { items = rows.Skip((page - 1) * pageSize).Take(pageSize), total = rows.Count, page, pageSize });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return UnknownResource();
        return Ok(await crm.GetAsync(kind, id, User));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string kind, [FromBody] RecordInput input)
    {
        if (!CrmUi.IsKind(kind)) return UnknownResource();
        var saved = await crm.SaveAsync(kind, null, input, User);
        return Created($"/api/{kind}/{saved.Id}", saved);
    }

    [HttpPut("{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Update(string kind, int id, [FromBody] RecordInput input)
    {
        if (!CrmUi.IsKind(kind)) return UnknownResource();
        return Ok(await crm.SaveAsync(kind, id, input, User));
    }

    [HttpDelete("{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string kind, int id)
    {
        if (!CrmUi.IsKind(kind)) return UnknownResource();
        await crm.DeleteAsync(kind, id, User);
        return NoContent();
    }

    [HttpPost("/api/leads/{id:int}/convert"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Convert(int id)
    {
        var customer = await crm.ConvertLeadAsync(id, User);
        return Created($"/api/customers/{customer.Id}", customer);
    }

    [HttpGet("/api/owners")]
    public async Task<IActionResult> Owners() => Ok(await crm.OwnersAsync(User));

    [HttpGet("/api/reports/pipeline")]
    public async Task<IActionResult> Pipeline([FromQuery] CrmFilter filter)
    {
        var opportunities = filter.Apply(await crm.ListAsync("opportunities", User), "opportunities").ToList();
        var open = opportunities.Where(r => r.Status == "Open").ToList();
        return Ok(new
        {
            totalOpenAmount = open.Sum(r => r.Amount),
            weightedPipeline = open.Sum(r => r.Amount * r.Probability / 100m),
            openCount = open.Count,
            wonCount = opportunities.Count(r => r.Status == "Won"),
            lostCount = opportunities.Count(r => r.Status == "Lost"),
            byStage = CrmUi.Stages.Select(stage => new { stage, count = opportunities.Count(r => r.Stage == stage), amount = opportunities.Where(r => r.Stage == stage).Sum(r => r.Amount), weightedAmount = open.Where(r => r.Stage == stage).Sum(r => r.Amount * r.Probability / 100m) }),
            byOwner = open.GroupBy(r => new { r.OwnerId, r.OwnerName }).Select(g => new { ownerId = g.Key.OwnerId, ownerName = g.Key.OwnerName, count = g.Count(), amount = g.Sum(r => r.Amount), weightedAmount = g.Sum(r => r.Amount * r.Probability / 100m) })
        });
    }

    private NotFoundObjectResult UnknownResource() => NotFound(new { error = "CRM resource not found.", errors = new Dictionary<string,string[]>() });
}
