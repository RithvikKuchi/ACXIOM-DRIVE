using AcxiomCRM.Models;
using AcxiomCRM.Services;

namespace AcxiomCRM.ViewModels;

public class DashboardViewModel
{
    public string Period { get; set; } = "all";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int TotalCustomers { get; set; }
    public int TotalLeads { get; set; }
    public int OpenLeads { get; set; }
    public int TotalOpportunities { get; set; }
    public int OpenOpportunities { get; set; }
    public int WonOpportunities { get; set; }
    public int LostOpportunities { get; set; }
    public decimal Pipeline { get; set; }
    public decimal WeightedPipeline { get; set; }
    public int PendingFollowUps { get; set; }
    public int OverdueFollowUps { get; set; }
    public int ActiveUsers { get; set; }
    public int SecurityEventsToday { get; set; }
    public string Scope { get; set; } = "";
    public string ChartJson { get; set; } = "{}";
    public List<RecordDto> Upcoming { get; set; } = [];
    public List<RecordDto> RecentLeads { get; set; } = [];
}

public class ReportViewModel
{
    public string Type { get; set; } = "pipeline";
    public string Title { get; set; } = "Pipeline report";
    public string Description { get; set; } = "";
    public string Query { get; set; } = "";
    public string Status { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Sort { get; set; }
    public bool Desc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / (double)PageSize));
    public List<OwnerOption> Owners { get; set; } = [];
    public List<string> Statuses { get; set; } = [];
    public List<string> Columns { get; set; } = [];
    public List<ReportRow> Rows { get; set; } = [];
    public Dictionary<string, string> Metrics { get; set; } = [];
}

public record ReportRow(string[] Cells);

public class AuditViewModel
{
    public string UserId { get; set; } = "";
    public string Module { get; set; } = "";
    public string ActionFilter { get; set; } = "";
    public string Query { get; set; } = "";
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public int Page { get; set; } = 1;
    public int Total { get; set; }
    public int Pages => Math.Max(1, (int)Math.Ceiling(Total / 20d));
    public List<AuditLog> Events { get; set; } = [];
    public Dictionary<string, string> Users { get; set; } = [];
    public List<string> Modules { get; set; } = [];
    public List<string> Actions { get; set; } = [];
}
