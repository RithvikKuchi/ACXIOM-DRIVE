using System.Threading.RateLimiting;
using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var cloudHosting = CloudHosting.Configure(builder);
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 10;
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    options.Lockout.AllowedForNewUsers = true;
}).AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders();
builder.Services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "AcxiomCRM.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 401;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Path.StartsWithSegments("/api")) context.Response.StatusCode = 403;
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnValidatePrincipal = async context =>
    {
        await SecurityStampValidator.ValidatePrincipalAsync(context);
        if (context.Principal?.Identity?.IsAuthenticated == true)
        {
            var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await manager.GetUserAsync(context.Principal);
            if (user is null || !user.IsActive)
            {
                context.RejectPrincipal();
                await context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>().SignOutAsync();
            }
        }
    };
});
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.Configure<ApiBehaviorOptions>(options =>
    options.InvalidModelStateResponseFactory = context => new BadRequestObjectResult(new
    {
        error = "Please correct the validation errors.",
        errors = context.ModelState.Where(x => x.Value?.Errors.Count > 0).ToDictionary(
            x => x.Key, x => x.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Enter a valid value." : e.ErrorMessage).ToArray())
    }));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "AcxiomCRM.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<CrmService>();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (cloudHosting) app.UseForwardedHeaders();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (exception is CrmException crm)
    {
        context.Response.StatusCode = crm.StatusCode;
        await context.Response.WriteAsJsonAsync(new { error = crm.Message, errors = crm.Errors });
    }
    else
    {
        context.Response.StatusCode = 500;
        if (context.Request.Path.StartsWithSegments("/api"))
            await context.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred. Please try again.", errors = new { } });
        else
        {
            context.Response.ContentType = "text/html";
            await context.Response.WriteAsync("<!doctype html><html lang='en'><head><title>AcxiomCRM</title></head><body><h1>We could not complete that request.</h1><p>Please try again or return to your dashboard.</p><a href='/'>Return to AcxiomCRM</a></body></html>");
        }
    }
}));
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Request.Path.StartsWithSegments("/api"))
    {
        var code = context.HttpContext.Response.StatusCode;
        var message = code switch
        {
            400 => "Invalid request or missing anti-forgery token.",
            401 => "Please sign in to continue.", 403 => "You do not have access to this resource.",
            404 => "The requested resource was not found.", 405 => "This HTTP method is not supported.",
            409 => "This operation conflicts with the current record.",
            429 => "Too many attempts. Please try again later.", _ => "The request could not be completed."
        };
        await context.HttpContext.Response.WriteAsJsonAsync(new { error = message, errors = new { } });
    }
});
// Managed hosting readiness probes use internal HTTP without a login or TLS redirect.
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/healthz" && (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method)))
    {
        context.Response.Headers.CacheControl = "no-store";
        var ready = false;
        try { ready = await context.RequestServices.GetRequiredService<ApplicationDbContext>().Database.CanConnectAsync(context.RequestAborted); }
        catch { /* Return only readiness, never infrastructure details. */ }
        context.Response.StatusCode = ready ? 200 : 503;
        if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.WriteAsJsonAsync(new { status = ready ? "ok" : "unavailable" });
        return;
    }
    await next();
});
if (!app.Environment.IsDevelopment()) { app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; object-src 'none'; base-uri 'self'; frame-ancestors 'none'; form-action 'self'";
    if (!context.Request.Path.StartsWithSegments("/lib") && !context.Request.Path.StartsWithSegments("/css") && !context.Request.Path.StartsWithSegments("/js"))
        context.Response.Headers.CacheControl = "no-store";
    await next();
});
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");
using (var scope = app.Services.CreateScope())
    await SeedData.InitializeAsync(scope.ServiceProvider, app.Configuration, app.Environment);
app.Run();

public partial class Program { }
