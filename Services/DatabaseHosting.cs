using AcxiomCRM.Data;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Services;

public static class DatabaseHosting
{
    public static bool IsPostgres(IConfiguration configuration) =>
        string.Equals(configuration["Database:Provider"], "PostgreSQL", StringComparison.OrdinalIgnoreCase);

    public static void Configure(WebApplicationBuilder builder)
    {
        var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";
        var postgres = IsPostgres(builder.Configuration);
        if (!postgres && !provider.Equals("Sqlite", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Database__Provider must be Sqlite or PostgreSQL.");
        var connection = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connection) || (postgres && connection.StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Set ConnectionStrings__DefaultConnection for the selected database provider.");
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            if (postgres) options.UseNpgsql(connection);
            else options.UseSqlite(connection);
        });
        if (postgres)
            builder.Services.AddDataProtection().SetApplicationName("AcxiomCRM").PersistKeysToDbContext<ApplicationDbContext>();
    }
}
