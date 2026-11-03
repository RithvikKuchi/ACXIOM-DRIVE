using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.RegularExpressions;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

public class CrmService(ApplicationDbContext db, UserManager<ApplicationUser> users, AuditService audit)
{
    public static readonly string[] Kinds = ["customers", "leads", "opportunities", "followups", "activities"];
    private static readonly string[] LeadStatuses = ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"];
    private static readonly string[] Stages = ["Qualification", "Proposal", "Negotiation", "Won", "Lost"];
    private static readonly string[] Types = ["Call", "Meeting", "Email", "Task"];
    private static readonly Dictionary<string, string[]> Transitions = new()
    {
        ["New"] = ["Contacted", "Unqualified", "Lost"], ["Contacted"] = ["Qualified", "Unqualified", "Lost"],
        ["Qualified"] = ["Contacted", "Unqualified", "Lost"], ["Unqualified"] = ["New"], ["Lost"] = ["New"], ["Converted"] = []
    };

    private static string UserId(ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true) throw new CrmException("Please sign in.", 401);
        if (!user.IsInRole("Admin") && !user.IsInRole("Manager") && !user.IsInRole("SalesExecutive"))
            throw new CrmException("Your account has no CRM permission.", 403);
        return user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new CrmException("Please sign in.", 401);
    }
    private IQueryable<CrmRecord> Query(string kind) => kind switch
    {
        "customers" => db.Customers, "leads" => db.Leads, "opportunities" => db.Opportunities,
        "followups" => db.FollowUps, "activities" => db.Activities,
        _ => throw new CrmException("This resource does not exist.", 404)
    };
    private IQueryable<CrmRecord> Scoped(string kind, ClaimsPrincipal user)
    {
        var userId = UserId(user);
        var query = Query(kind);
        return user.IsInRole("Admin") || user.IsInRole("Manager") ? query : query.Where(x => x.OwnerId == userId);
    }

    public async Task<List<RecordDto>> ListAsync(string kind, ClaimsPrincipal user) =>
        (await Scoped(kind, user).AsNoTracking().Include(x => x.Owner).OrderByDescending(x => x.CreatedDate).ToListAsync()).Select(ToDto).ToList();
    public async Task<RecordDto> GetAsync(string kind, int id, ClaimsPrincipal user) => ToDto(await FindAsync(kind, id, user));
    private async Task<CrmRecord> FindAsync(string kind, int id, ClaimsPrincipal user) =>
        await Scoped(kind, user).Include(x => x.Owner).FirstOrDefaultAsync(x => x.Id == id) ?? throw new CrmException("The record was not found in your authorized scope.", 404);

    public async Task<List<OwnerOption>> OwnersAsync(ClaimsPrincipal user)
    {
        var id = UserId(user);
        if (!user.IsInRole("Admin") && !user.IsInRole("Manager"))
        {
            var owner = await users.FindByIdAsync(id);
            return owner is { IsActive: true } ? [new(owner.Id, owner.Name)] : [];
        }
        return (await users.GetUsersInRoleAsync("SalesExecutive")).Where(x => x.IsActive).OrderBy(x => x.Name).Select(x => new OwnerOption(x.Id, x.Name)).ToList();
    }

    public async Task<RecordDto> SaveAsync(string kind, int? id, RecordInput input, ClaimsPrincipal user)
    {
        var actor = UserId(user);
        var entity = id.HasValue ? await FindAsync(kind, id.Value, user) : NewEntity(kind);
        var before = id.HasValue ? ToDto(entity) : null;
        Normalize(input);
        await ValidateAsync(kind, input, entity, id, user);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            CopyInput(input, entity);
            if (entity is Lead lead && before != null) lead.CustomerId = before.CustomerId;
            if (entity is Opportunity opportunity)
            {
                if (opportunity.Stage is "Won" or "Lost")
                {
                    opportunity.Status = opportunity.Stage;
                    opportunity.Probability = opportunity.Stage == "Won" ? 100 : 0;
                    if (before == null || before.Stage != opportunity.Stage) opportunity.ClosedDate = DateTime.UtcNow;
                }
                else { opportunity.Status = "Open"; opportunity.ClosedDate = null; }
            }
            entity.Owner = await users.FindByIdAsync(input.OwnerId);
            if (!id.HasValue) { entity.CreatedBy = actor; entity.CreatedDate = DateTime.UtcNow; db.Add(entity); }
            else entity.ModifiedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            var after = ToDto(entity);
            await audit.LogAsync(id.HasValue ? "Update" : "Create", kind, entity.Id.ToString(), before, after);
            if (before != null && before.Status != after.Status)
                await audit.LogAsync("StatusChange", kind, entity.Id.ToString(), new { before.Status }, new { after.Status });
            if (before != null && kind == "followups" && before.FollowUpDate != after.FollowUpDate)
                await audit.LogAsync("Reschedule", kind, entity.Id.ToString(), new { before.FollowUpDate }, new { after.FollowUpDate });
            await transaction.CommitAsync();
            return await GetAsync(kind, entity.Id, user);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(); db.ChangeTracker.Clear();
            throw new CrmException("This change conflicts with an existing record. Check unique email, phone and related records.", 409);
        }
    }

    public async Task DeleteAsync(string kind, int id, ClaimsPrincipal user)
    {
        var entity = await FindAsync(kind, id, user);
        if (entity is Lead { Status: "Converted" }) throw new CrmException("A converted lead is retained for conversion history.", 409);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var before = ToDto(entity); db.Remove(entity); await db.SaveChangesAsync();
            await audit.LogAsync("Delete", kind, id.ToString(), before);
            await transaction.CommitAsync();
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(); db.ChangeTracker.Clear();
            throw new CrmException("This record has related records. Remove those links first, or change its status instead.", 409);
        }
    }

    public async Task<RecordDto> ConvertLeadAsync(int id, ClaimsPrincipal user)
    {
        var lead = (Lead)await FindAsync("leads", id, user);
        if (lead.Status != "Qualified" || lead.CustomerId.HasValue)
            throw new CrmException("Only a qualified, unconverted lead can be converted.", 409);
        if (await db.Customers.AnyAsync(x => x.Email == lead.Email || x.Phone == lead.Phone))
            throw new CrmException("A customer with this email or phone already exists. Resolve the duplicate before conversion.", 409);
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            var before = ToDto(lead);
            var customer = new Customer
            {
                Name = lead.Name, Email = lead.Email, Phone = lead.Phone, CompanyName = lead.CompanyName,
                Notes = lead.Notes, OwnerId = lead.OwnerId, CreatedBy = UserId(user), Status = "Active"
            };
            db.Customers.Add(customer); await db.SaveChangesAsync();
            lead.Status = "Converted"; lead.CustomerId = customer.Id; lead.ModifiedDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await audit.LogAsync("Create", "customers", customer.Id.ToString(), null, ToDto(customer));
            await audit.LogAsync("Convert", "leads", lead.Id.ToString(), before, ToDto(lead));
            await transaction.CommitAsync();
            return await GetAsync("customers", customer.Id, user);
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(); db.ChangeTracker.Clear();
            throw new CrmException("The conversion conflicts with another change. Refresh the lead and try again.", 409);
        }
    }

    private async Task ValidateAsync(string kind, RecordInput x, CrmRecord existing, int? id, ClaimsPrincipal user)
    {
        var errors = new Dictionary<string, string[]>();
        void Error(string key, string message) => errors[key] = [message];
        void Required(string key, string value) { if (string.IsNullOrWhiteSpace(value)) Error(key, $"{key} is required."); }
        void Allowed(string key, string value, string[] values) { if (!values.Contains(value)) Error(key, $"Choose a valid {key.ToLowerInvariant()}."); }
        var max = new Dictionary<string, int> { ["Name"] = 150, ["Email"] = 254, ["Phone"] = 10, ["CompanyName"] = 150,
            ["Address"] = 500, ["City"] = 80, ["State"] = 80, ["Notes"] = 2000, ["Description"] = 2000,
            ["Source"] = 80, ["Subject"] = 150, ["Priority"] = 20, ["Status"] = 30, ["Stage"] = 30, ["Type"] = 30, ["OwnerId"] = 450 };
        foreach (var p in typeof(RecordInput).GetProperties().Where(p => p.PropertyType == typeof(string)))
            if (max.TryGetValue(p.Name, out var limit) && ((string?)p.GetValue(x))?.Length > limit) Error(p.Name, $"{p.Name} must be {limit} characters or fewer.");

        var actor = UserId(user);
        if (!user.IsInRole("Admin") && !user.IsInRole("Manager"))
        {
            if (!string.IsNullOrEmpty(x.OwnerId) && x.OwnerId != actor) throw new CrmException("You cannot assign records to another user.", 403);
            x.OwnerId = actor;
        }
        Required("OwnerId", x.OwnerId);
        var owner = string.IsNullOrEmpty(x.OwnerId) ? null : await users.FindByIdAsync(x.OwnerId);
        if (owner == null || !owner.IsActive || !await users.IsInRoleAsync(owner, "SalesExecutive")) Error("OwnerId", "Choose an active sales executive.");

        if (kind is "customers" or "leads")
        {
            Required("Name", x.Name); Required("Email", x.Email); Required("Phone", x.Phone);
            if (!new EmailAddressAttribute().IsValid(x.Email)) Error("Email", "Enter a valid email address.");
            if (!Regex.IsMatch(x.Phone, "^[6-9][0-9]{9}$")) Error("Phone", "Enter a 10-digit Indian mobile number starting with 6, 7, 8 or 9.");
        }
        if (kind == "customers")
        {
            Allowed("Status", x.Status, ["Active", "Inactive"]);
            if (await db.Customers.AnyAsync(c => c.Id != id && c.Email == x.Email)) Error("Email", "A customer with this email already exists.");
            if (await db.Customers.AnyAsync(c => c.Id != id && c.Phone == x.Phone)) Error("Phone", "A customer with this phone already exists.");
        }
        if (kind == "leads")
        {
            Allowed("Status", x.Status, LeadStatuses); Allowed("Priority", x.Priority, ["Low", "Medium", "High"]);
            if (x.ExpectedValue < 0 || x.ExpectedValue > 999999999.99m || decimal.Round(x.ExpectedValue, 2) != x.ExpectedValue)
                Error("ExpectedValue", "Expected value must be from 0 to 999999999.99 with at most 2 decimals.");
            if (!id.HasValue && x.Status != "New") Error("Status", "New leads must start with New status.");
            if (id.HasValue && existing.Status == "Converted") throw new CrmException("Converted leads are retained as read-only conversion history.", 409);
            if (id.HasValue && existing.Status != x.Status && (!Transitions.TryGetValue(existing.Status, out var next) || !next.Contains(x.Status)))
                Error("Status", $"A lead cannot move from {existing.Status} to {x.Status}. Use the lead conversion action for conversion.");
            if (x.CustomerId != (existing as Lead)?.CustomerId && x.CustomerId.HasValue) Error("CustomerId", "Use the conversion action to link a lead to its customer.");
        }
        if (kind == "opportunities")
        {
            Required("Name", x.Name); Allowed("Stage", x.Stage, Stages); Allowed("Status", x.Status, ["Open", "Won", "Lost"]);
            if ((x.Stage is "Won" or "Lost") ? x.Status != x.Stage : x.Status != "Open") Error("Status", "Status must match the stage: Open for active stages, Won or Lost for final stages.");
            if (x.Amount <= 0 || x.Amount > 999999999.99m || decimal.Round(x.Amount, 2) != x.Amount)
                Error("Amount", "Opportunity amount must be greater than 0 and no more than 999999999.99, with at most 2 decimals.");
            if (x.Probability < 0 || x.Probability > 100) Error("Probability", "Probability must be between 0 and 100.");
            if (!x.ExpectedCloseDate.HasValue) Error("ExpectedCloseDate", "Expected close date is required.");
            else if (x.Stage is not ("Won" or "Lost") && x.ExpectedCloseDate.Value.Date < DateTime.Today) Error("ExpectedCloseDate", "Expected close date cannot be in the past for an active opportunity.");
            if (!x.CustomerId.HasValue) Error("CustomerId", "Choose a customer.");
        }
        if (kind == "followups")
        {
            Required("Subject", x.Subject); Allowed("Type", x.Type, Types); Allowed("Status", x.Status, ["Planned", "Completed", "Missed", "Cancelled"]);
            if (!x.FollowUpDate.HasValue) Error("FollowUpDate", "Follow-up date is required.");
            else if ((!id.HasValue || x.Status == "Planned") && x.FollowUpDate.Value.Date < DateTime.Today) Error("FollowUpDate", "Follow-up date cannot be earlier than today.");
            if (!x.CustomerId.HasValue && !x.LeadId.HasValue && !x.OpportunityId.HasValue) Error("CustomerId", "Select a related customer, lead or opportunity.");
            x.Name = x.Subject;
        }
        if (kind == "activities")
        {
            Required("Subject", x.Subject); Allowed("Type", x.Type, Types); Allowed("Status", x.Status, ["Planned", "Completed", "Cancelled"]);
            if (!x.ActivityDate.HasValue) Error("ActivityDate", "Activity date is required.");
            x.Name = x.Subject;
        }
        if (kind is "opportunities" or "followups" or "activities")
        {
            await ValidateReference("customers", x.CustomerId, "CustomerId");
            await ValidateReference("leads", x.LeadId, "LeadId");
            if (kind is "followups" or "activities") await ValidateReference("opportunities", x.OpportunityId, "OpportunityId");
        }
        if (errors.Count > 0) throw new CrmException("Please correct the highlighted fields.", 400, errors);

        async Task ValidateReference(string referenceKind, int? referenceId, string field)
        {
            if (!referenceId.HasValue) return;
            var reference = await Scoped(referenceKind, user).AsNoTracking().FirstOrDefaultAsync(r => r.Id == referenceId.Value);
            if (reference == null) Error(field, "The related record does not exist in your authorized scope.");
            else if (reference.OwnerId != x.OwnerId) Error(field, "The related record must belong to the selected sales executive.");
        }
    }

    private static CrmRecord NewEntity(string kind) => kind switch
    { "customers" => new Customer(), "leads" => new Lead(), "opportunities" => new Opportunity(), "followups" => new FollowUp(), "activities" => new Activity(), _ => throw new CrmException("This resource does not exist.", 404) };
    private static void Normalize(RecordInput input)
    {
        foreach (var p in typeof(RecordInput).GetProperties().Where(p => p.PropertyType == typeof(string))) p.SetValue(input, ((string?)p.GetValue(input) ?? "").Trim());
        input.Email = input.Email.ToLowerInvariant();
    }
    private static void CopyInput(RecordInput input, CrmRecord entity)
    {
        foreach (var p in typeof(RecordInput).GetProperties())
        {
            var target = entity.GetType().GetProperty(p.Name);
            if (target?.CanWrite == true && target.PropertyType == p.PropertyType) target.SetValue(entity, p.GetValue(input));
        }
    }
    private static RecordDto ToDto(CrmRecord entity)
    {
        var dto = new RecordDto();
        foreach (var p in typeof(RecordDto).GetProperties())
        {
            var source = entity.GetType().GetProperty(p.Name);
            if (source?.CanRead == true && source.PropertyType == p.PropertyType) p.SetValue(dto, source.GetValue(entity));
        }
        dto.OwnerName = entity.Owner?.Name ?? "";
        dto.Code = entity switch { Customer c => c.CustomerCode, Lead l => l.LeadCode, _ => $"{entity.GetType().Name[..3].ToUpperInvariant()}-{entity.Id:D5}" };
        return dto;
    }
}
