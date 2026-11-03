using System.ComponentModel.DataAnnotations;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcxiomCRM.Controllers.Api;

public class ApiLoginInput
{
    [Required, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128)] public string Password { get; set; } = "";
}

[ApiController]
[Route("api/auth")]
public class AuthApiController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AuditService audit, IAntiforgery antiforgery) : ControllerBase
{
    [HttpGet("csrf"), AllowAnonymous]
    public IActionResult Csrf()
    {
        Response.Headers.CacheControl = "no-store";
        var tokens = antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken, headerName = "X-CSRF-TOKEN" });
    }

    [HttpPost("login"), AllowAnonymous, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] ApiLoginInput input)
    {
        var account = input.Email.Trim();
        var user = await users.FindByEmailAsync(account) ?? await users.FindByNameAsync(account);
        var result = user is { IsActive: true }
            ? await signIn.PasswordSignInAsync(user, input.Password, isPersistent: false, lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        if (!result.Succeeded)
        {
            await audit.LogAsync(result.IsLockedOut ? "Lockout" : "FailedLogin", "Authentication", user?.Id, result: "Failed");
            return Unauthorized(new { error = "Sign-in failed. Check your credentials or try again after the lockout period.", errors = new Dictionary<string,string[]>() });
        }
        await audit.LogAsync("Login", "Authentication", user!.Id);
        return Ok(new { name = user.Name, email = user.Email, role = (await users.GetRolesAsync(user)).SingleOrDefault(), message = "Signed in. Request a new CSRF token before sending authenticated writes." });
    }

    [HttpPost("logout"), Authorize, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await audit.LogAsync("Logout", "Authentication", users.GetUserId(User));
        await signIn.SignOutAsync();
        return Ok(new { message = "Signed out." });
    }

    [HttpGet("me"), Authorize]
    public async Task<IActionResult> Me()
    {
        var user = await users.GetUserAsync(User);
        if (user is not { IsActive: true }) return Unauthorized(new { error = "Authentication is required.", errors = new Dictionary<string,string[]>() });
        return Ok(new { id = user.Id, name = user.Name, email = user.Email, role = (await users.GetRolesAsync(user)).SingleOrDefault() });
    }
}
