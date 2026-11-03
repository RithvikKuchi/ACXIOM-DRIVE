using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace AcxiomCRM.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(100)] public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}

public abstract class CrmRecord
{
    public int Id { get; set; }
    [MaxLength(150)] public string Name { get; set; } = "";
    [MaxLength(30)] public string Status { get; set; } = "";
    [MaxLength(2000)] public string Notes { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public ApplicationUser? Owner { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedDate { get; set; }
}

public class Customer : CrmRecord
{
    [MaxLength(30)] public string CustomerCode { get; set; } = "CUS-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(20)] public string Phone { get; set; } = "";
    [MaxLength(150)] public string CompanyName { get; set; } = "";
    [MaxLength(500)] public string Address { get; set; } = "";
    [MaxLength(80)] public string City { get; set; } = "";
    [MaxLength(80)] public string State { get; set; } = "";
}
public class Lead : CrmRecord
{
    [MaxLength(30)] public string LeadCode { get; set; } = "LEAD-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(20)] public string Phone { get; set; } = "";
    [MaxLength(150)] public string CompanyName { get; set; } = "";
    [MaxLength(80)] public string Source { get; set; } = "";
    [MaxLength(20)] public string Priority { get; set; } = "Medium";
    public decimal ExpectedValue { get; set; }
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
public class Opportunity : CrmRecord
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public decimal Amount { get; set; }
    [MaxLength(30)] public string Stage { get; set; } = "Qualification";
    public int Probability { get; set; }
    public DateTime? ExpectedCloseDate { get; set; }
    public DateTime? ClosedDate { get; set; }
    [MaxLength(80)] public string Source { get; set; } = "";
}
public class FollowUp : CrmRecord
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public int? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }
    public DateTime? FollowUpDate { get; set; }
    [MaxLength(30)] public string Type { get; set; } = "Call";
    [MaxLength(150)] public string Subject { get; set; } = "";
}
public class Activity : CrmRecord
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public int? LeadId { get; set; }
    public Lead? Lead { get; set; }
    public int? OpportunityId { get; set; }
    public Opportunity? Opportunity { get; set; }
    [MaxLength(30)] public string Type { get; set; } = "Task";
    [MaxLength(150)] public string Subject { get; set; } = "";
    [MaxLength(2000)] public string Description { get; set; } = "";
    public DateTime? ActivityDate { get; set; }
}
public class AuditLog
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    [MaxLength(60)] public string Action { get; set; } = "";
    [MaxLength(60)] public string EntityName { get; set; } = "";
    [MaxLength(100)] public string? RecordId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
    [MaxLength(80)] public string? IpAddress { get; set; }
    [MaxLength(30)] public string Result { get; set; } = "Success";
}
