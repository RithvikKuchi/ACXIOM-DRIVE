using AcxiomCRM.Models;

namespace AcxiomCRM.ViewModels;

public static class CrmUi
{
    public static readonly string[] Kinds = ["customers", "leads", "opportunities", "followups", "activities"];
    public static bool IsKind(string kind) => Kinds.Contains(kind);
    public static string Title(string kind) => kind switch { "customers" => "Customers", "leads" => "Leads", "opportunities" => "Opportunities", "followups" => "Follow-ups", "activities" => "Activities", _ => "Records" };
    public static string Singular(string kind) => kind switch { "customers" => "customer", "leads" => "lead", "opportunities" => "opportunity", "followups" => "follow-up", "activities" => "activity", _ => "record" };
    public static string Entity(string kind) => kind switch { "customers" => "Customer", "leads" => "Lead", "opportunities" => "Opportunity", "followups" => "FollowUp", "activities" => "Activity", _ => "" };
    public static string[] Statuses(string kind) => kind switch
    {
        "customers" => ["Active", "Inactive"],
        "leads" => ["New", "Contacted", "Qualified", "Unqualified", "Converted", "Lost"],
        "opportunities" => ["Open", "Won", "Lost"],
        "followups" => ["Planned", "Completed", "Missed", "Cancelled"],
        "activities" => ["Planned", "Completed", "Cancelled"],
        _ => []
    };
    public static readonly string[] Stages = ["Qualification", "Proposal", "Negotiation", "Won", "Lost"];
    public static readonly string[] Types = ["Call", "Meeting", "Email", "Task"];
    public static string Label(RecordDto item) => !string.IsNullOrWhiteSpace(item.Name) ? item.Name : item.Subject;
    public static string Badge(string status) => status switch
    {
        "Active" or "Won" or "Completed" or "Converted" => "bg-success-subtle text-success-emphasis",
        "Lost" or "Missed" or "Inactive" or "Unqualified" => "bg-danger-subtle text-danger-emphasis",
        "Qualified" or "Negotiation" => "bg-info-subtle text-info-emphasis",
        _ => "bg-primary-subtle text-primary-emphasis"
    };
    public static DateTime Date(RecordDto row, string kind) => kind switch { "followups" => row.FollowUpDate ?? row.CreatedDate, "activities" => row.ActivityDate ?? row.CreatedDate, "opportunities" => row.ExpectedCloseDate ?? row.CreatedDate, _ => row.CreatedDate };
    public static string[] EditableStatuses(string kind, string current, bool create)
    {
        if (kind != "leads") return Statuses(kind);
        if (create) return ["New"];
        return current switch
        {
            "New" => ["New", "Contacted", "Unqualified", "Lost"],
            "Contacted" => ["Contacted", "Qualified", "Unqualified", "Lost"],
            "Qualified" => ["Qualified", "Contacted", "Unqualified", "Lost"],
            "Unqualified" => ["Unqualified", "New"],
            "Lost" => ["Lost", "New"],
            _ => [current]
        };
    }
}

public class CrmFilter
{
    public string? Q { get; set; }
    public string? Status { get; set; }
    public string? OwnerId { get; set; }
    public string? Stage { get; set; }
    public string? Type { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public string Sort { get; set; } = "newest";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public bool Overdue { get; set; }
    public int? CustomerId { get; set; }
    public int? LeadId { get; set; }

    public IEnumerable<RecordDto> Apply(IEnumerable<RecordDto> source, string kind, IReadOnlyDictionary<int,string>? customerNames = null)
    {
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            source = source.Where(r => new[] { r.Name, r.Subject, r.Email, r.Phone, r.CompanyName, r.Code, r.OwnerName, r.CustomerId.HasValue && customerNames != null ? customerNames.GetValueOrDefault(r.CustomerId.Value, "") : "" }.Any(v => v?.Contains(term, StringComparison.OrdinalIgnoreCase) == true));
        }
        if (!string.IsNullOrWhiteSpace(Status)) source = source.Where(r => r.Status == Status);
        if (!string.IsNullOrWhiteSpace(OwnerId)) source = source.Where(r => r.OwnerId == OwnerId);
        if (!string.IsNullOrWhiteSpace(Stage)) source = source.Where(r => r.Stage == Stage);
        if (!string.IsNullOrWhiteSpace(Type)) source = source.Where(r => r.Type == Type);
        if (From.HasValue) source = source.Where(r => CrmUi.Date(r, kind).Date >= From.Value.Date);
        if (To.HasValue) source = source.Where(r => CrmUi.Date(r, kind).Date <= To.Value.Date);
        if (CustomerId.HasValue) source = source.Where(r => r.CustomerId == CustomerId);
        if (LeadId.HasValue) source = source.Where(r => r.LeadId == LeadId);
        if (Overdue) source = source.Where(r => r.Status == "Planned" && r.FollowUpDate?.Date < DateTime.Today);
        return Sort switch
        {
            "name" => source.OrderBy(CrmUi.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
            "name_desc" => source.OrderByDescending(CrmUi.Label, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Id),
            "date" => source.OrderBy(r => CrmUi.Date(r, kind)).ThenBy(r => r.Id),
            "amount" => source.OrderByDescending(r => kind == "leads" ? r.ExpectedValue : r.Amount).ThenBy(r => r.Id),
            "status" => source.OrderBy(r => r.Status).ThenBy(r => r.Id),
            _ => source.OrderByDescending(r => r.CreatedDate).ThenByDescending(r => r.Id)
        };
    }
}

public class CrmListModel
{
    public string Kind { get; set; } = "customers";
    public CrmFilter Filter { get; set; } = new();
    public List<RecordDto> Items { get; set; } = [];
    public List<OwnerOption> Owners { get; set; } = [];
    public List<RecordDto> Customers { get; set; } = [];
    public List<RecordDto> Leads { get; set; } = [];
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)Filter.PageSize));
}

public class CrmFormModel
{
    public string Kind { get; set; } = "customers";
    public int? Id { get; set; }
    public RecordInput Input { get; set; } = new();
    public string OriginalStatus { get; set; } = "";
    public List<OwnerOption> Owners { get; set; } = [];
    public List<RecordDto> Customers { get; set; } = [];
    public List<RecordDto> Leads { get; set; } = [];
    public List<RecordDto> Opportunities { get; set; } = [];
}

public record RecordEvent(string Action, DateTime CreatedDate, string Result);
public class CrmDetailsModel
{
    public string Kind { get; set; } = "customers";
    public RecordDto Item { get; set; } = new();
    public List<RecordEvent> History { get; set; } = [];
    public List<RecordDto> FollowUps { get; set; } = [];
    public List<RecordDto> Activities { get; set; } = [];
    public Dictionary<int,string> CustomerNames { get; set; } = [];
    public Dictionary<int,string> LeadNames { get; set; } = [];
}
