namespace AcxiomCRM.Models;

// Boundary DTOs deliberately exclude entity navigation, creator and security properties.
public class RecordInput
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string CompanyName { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string Status { get; set; } = "";
    public string Source { get; set; } = "";
    public string Priority { get; set; } = "";
    public string Notes { get; set; } = "";
    public string Stage { get; set; } = "";
    public string Type { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public decimal Amount { get; set; }
    public decimal ExpectedValue { get; set; }
    public int Probability { get; set; }
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }
    public int? OpportunityId { get; set; }
    public DateTime? ExpectedCloseDate { get; set; }
    public DateTime? FollowUpDate { get; set; }
    public DateTime? ActivityDate { get; set; }
}
public class RecordDto : RecordInput
{
    public DateTime? ClosedDate { get; set; }
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string OwnerName { get; set; } = "";
    public DateTime CreatedDate { get; set; }
    public DateTime? ModifiedDate { get; set; }
}
public record OwnerOption(string Id, string Name);
