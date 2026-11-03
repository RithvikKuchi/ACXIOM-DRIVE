using System.Security.Claims;
using System.Text.Json;
using AcxiomCRM.Data;
using AcxiomCRM.Models;

namespace AcxiomCRM.Services;

public class AuditService(ApplicationDbContext db, IHttpContextAccessor accessor)
{
    public async Task LogAsync(string action, string entityName, string? recordId = null, object? oldValue = null, object? newValue = null, string result = "Success")
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? (entityName == "Authentication" ? recordId : null),
            Action = action, EntityName = entityName, RecordId = recordId,
            OldValue = SafeJson(oldValue), NewValue = SafeJson(newValue), Result = result,
            IpAddress = accessor.HttpContext?.Connection.RemoteIpAddress?.ToString(), CreatedDate = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static string? SafeJson(object? value)
    {
        if (value is null) return null;
        // Never serialize Identity entities or request models containing credential fields.
        var forbidden = new[] { "password", "token", "secret", "securitystamp", "concurrencystamp" };
        var safe = new Dictionary<string, object?>();
        foreach (var property in value.GetType().GetProperties())
        {
            if (forbidden.Any(s => property.Name.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            if (property.GetIndexParameters().Length > 0) continue;
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (type == typeof(string) || type.IsPrimitive || type == typeof(decimal) || type == typeof(DateTime) || type == typeof(DateTimeOffset))
                safe[property.Name] = property.GetValue(value);
        }
        return JsonSerializer.Serialize(safe);
    }
}
