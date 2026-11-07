using AcxiomCRM.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration, IWebHostEnvironment environment)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        await db.Database.EnsureCreatedAsync();
        // Constant SQL only: protect append-only audit rows even from accidental data-layer edits.
        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE OR REPLACE FUNCTION acxiom_reject_audit_change() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Audit records are append-only'; END; $$;
                CREATE OR REPLACE TRIGGER "AuditLogs_AppendOnly"
                BEFORE UPDATE OR DELETE ON "AuditLogs"
                FOR EACH ROW EXECUTE FUNCTION acxiom_reject_audit_change();
                """);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER IF NOT EXISTS AuditLogs_NoUpdate BEFORE UPDATE ON AuditLogs BEGIN SELECT RAISE(ABORT, 'Audit records are append-only'); END;");
            await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER IF NOT EXISTS AuditLogs_NoDelete BEFORE DELETE ON AuditLogs BEGIN SELECT RAISE(ABORT, 'Audit records are append-only'); END;");
        }
        foreach (var role in new[] { "Admin", "Manager", "SalesExecutive" })
            if (!await roles.RoleExistsAsync(role)) Require(await roles.CreateAsync(new IdentityRole(role)));

        if (!environment.IsDevelopment())
        {
            var email = configuration["Bootstrap:Email"];
            var password = configuration["Bootstrap:Password"];
            if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(password) && !(await users.GetUsersInRoleAsync("Admin")).Any())
                await CreateUser(email, configuration["Bootstrap:Name"] ?? "Administrator", "Admin", password);
            return;
        }
        var seedPassword = configuration["Seed:Password"];
        if (string.IsNullOrWhiteSpace(seedPassword)) return;
        var admin = await CreateUser("admin@acxiom.local", "Alex Morgan", "Admin", seedPassword);
        await CreateUser("manager@acxiom.local", "Priya Sharma", "Manager", seedPassword);
        var sales = await CreateUser("sales@acxiom.local", "Rohan Mehta", "SalesExecutive", seedPassword);
        var sales2 = await CreateUser("sales2@acxiom.local", "Ananya Rao", "SalesExecutive", seedPassword);
        if (await db.Customers.AnyAsync()) return;

        var companies = new[] { "Northstar Technologies", "Evergreen Foods", "Vertex Logistics", "Cobalt Health", "Horizon Retail", "Summit Manufacturing", "Bluebird Media", "Pioneer Energy" };
        var contacts = new[] { "Arjun Patel", "Neha Kapoor", "Vikram Singh", "Aditi Shah", "Rahul Iyer", "Meera Nair", "Karan Joshi", "Sana Khan" };
        var customers = new List<Customer>();
        for (var i = 0; i < companies.Length; i++)
        {
            customers.Add(new Customer
            {
                Name = contacts[i], CompanyName = companies[i], Email = $"contact{i + 1}@example.com", Phone = $"98765432{i:D2}",
                Address = $"{i + 10}, Business Park", City = i % 2 == 0 ? "Bengaluru" : "Mumbai", State = i % 2 == 0 ? "Karnataka" : "Maharashtra",
                Status = i == 7 ? "Inactive" : "Active", OwnerId = i % 2 == 0 ? sales.Id : sales2.Id, CreatedBy = admin.Id,
                CreatedDate = DateTime.UtcNow.AddDays(-i * 4 - 2), Notes = "Demo account. Review requirements during the next customer meeting."
            });
        }
        db.Customers.AddRange(customers); await db.SaveChangesAsync();
        var leads = new List<Lead>();
        var statuses = new[] { "New", "Contacted", "Qualified", "New", "Lost", "Qualified", "Contacted", "Unqualified" };
        for (var i = 0; i < 8; i++)
        {
            leads.Add(new Lead
            {
                Name = new[] { "Dev Malhotra", "Isha Gupta", "Nikhil Bose", "Tara Reddy", "Aarav Kumar", "Riya Sethi", "Varun Das", "Pooja Jain" }[i],
                CompanyName = new[] { "Atlas Systems", "Brightlane Studios", "Cedar Consulting", "Orbit Solutions", "Vista Digital", "Lumina Labs", "Oakridge Ventures", "Prism Works" }[i],
                Email = $"lead{i + 1}@example.com", Phone = $"91234567{i:D2}", Source = i % 2 == 0 ? "Website" : "Referral",
                Priority = i % 3 == 0 ? "High" : "Medium", Status = statuses[i], ExpectedValue = 65000 + i * 15000,
                OwnerId = i % 2 == 0 ? sales.Id : sales2.Id, CreatedBy = admin.Id, CreatedDate = DateTime.UtcNow.AddDays(-i * 3),
                Notes = "Discuss scope and schedule a discovery call."
            });
        }
        db.Leads.AddRange(leads);
        var stages = new[] { "Qualification", "Proposal", "Negotiation", "Won", "Lost", "Proposal", "Negotiation", "Won" };
        for (var i = 0; i < customers.Count; i++)
        {
            var stage = stages[i];
            db.Opportunities.Add(new Opportunity
            {
                Name = new[] { "Enterprise rollout", "Customer success platform", "Logistics automation", "Clinic workflow upgrade", "Retail pilot", "Factory analytics", "Media operations suite", "Energy insights" }[i],
                CustomerId = customers[i].Id, OwnerId = customers[i].OwnerId, CreatedBy = admin.Id,
                Amount = 120000 + 45000 * i, Stage = stage, Status = stage is "Won" or "Lost" ? stage : "Open",
                Probability = stage == "Won" ? 100 : stage == "Lost" ? 0 : stage == "Negotiation" ? 75 : stage == "Proposal" ? 50 : 25,
                ExpectedCloseDate = DateTime.Today.AddDays(stage is "Won" or "Lost" ? -i * 7 : 14 + i * 3),
                ClosedDate = stage is "Won" or "Lost" ? DateTime.UtcNow.AddMonths(-(i % 3)) : null,
                CreatedDate = DateTime.UtcNow.AddDays(-10 - i * 6), Source = "Referral", Notes = "Demo opportunity with a scoped owner and customer."
            });
        }
        await db.SaveChangesAsync();
        for (var i = 0; i < 8; i++)
        {
            db.FollowUps.Add(new FollowUp
            {
                Name = "Customer check-in", Subject = new[] { "Review proposal", "Schedule discovery", "Share quotation", "Confirm handover" }[i % 4],
                CustomerId = customers[i].Id, OwnerId = customers[i].OwnerId, CreatedBy = admin.Id,
                FollowUpDate = DateTime.Today.AddDays(i - 2), Type = i % 2 == 0 ? "Call" : "Meeting",
                Status = i == 1 ? "Completed" : "Planned", Notes = "Discuss next steps and update the account notes.", CreatedDate = DateTime.UtcNow.AddDays(-5)
            });
            db.Activities.Add(new Activity
            {
                Name = "Account discovery", Subject = "Discovery meeting", Description = "Captured objectives, stakeholders, timeline and budget.",
                CustomerId = customers[i].Id, OwnerId = customers[i].OwnerId, CreatedBy = admin.Id,
                ActivityDate = DateTime.Today.AddDays(-i), Type = i % 2 == 0 ? "Meeting" : "Call", Status = "Completed", CreatedDate = DateTime.UtcNow.AddDays(-i)
            });
        }
        db.AuditLogs.Add(new AuditLog { UserId = admin.Id, Action = "Seed", EntityName = "System", Result = "Success", NewValue = "{\"description\":\"Development demonstration records created\"}" });
        await db.SaveChangesAsync();

        async Task<ApplicationUser> CreateUser(string email, string name, string role, string password)
        {
            var existing = await users.FindByEmailAsync(email); if (existing != null) return existing;
            var user = new ApplicationUser { UserName = email, Email = email, Name = name, EmailConfirmed = true, IsActive = true };
            Require(await users.CreateAsync(user, password)); Require(await users.AddToRoleAsync(user, role)); return user;
        }
    }
    private static void Require(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Account initialization failed: " + string.Join("; ", result.Errors.Select(x => x.Description)));
    }
}
