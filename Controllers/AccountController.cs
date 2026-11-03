using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AcxiomCRM.Controllers;

[Authorize]
public class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AuditService audit, ApplicationDbContext db, IConfiguration configuration) : Controller
{
    [AllowAnonymous, HttpGet]
    public IActionResult Login(string? returnUrl = null) => User.Identity?.IsAuthenticated == true ? RedirectToAction("Index", "Dashboard") : View(new LoginViewModel { ReturnUrl = returnUrl });

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var value = model.Email.Trim();
        var user = await users.FindByEmailAsync(value) ?? await users.FindByNameAsync(value);
        var result = user is { IsActive: true }
            ? await signIn.PasswordSignInAsync(user, model.Password, model.RememberMe, lockoutOnFailure: true)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        if (!result.Succeeded)
        {
            await audit.LogAsync(result.IsLockedOut ? "Lockout" : "FailedLogin", "Authentication", user?.Id, result: "Failed");
            ModelState.AddModelError("", result.IsLockedOut ? "This account is temporarily locked. Please try again later." : "Unable to sign in with these credentials.");
            return View(model);
        }
        await audit.LogAsync("Login", "Authentication", user!.Id);
        return !string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl) ? LocalRedirect(model.ReturnUrl) : RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult Register() => configuration.GetValue("Authentication:AllowRegistration", true) ? View(new RegisterViewModel()) : NotFound();

    [AllowAnonymous, HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!configuration.GetValue("Authentication:AllowRegistration", true)) return NotFound();
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Full name is required.");
        if (!ModelState.IsValid) return View(model);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var user = new ApplicationUser { Name = model.Name.Trim(), Email = model.Email.Trim().ToLowerInvariant(), UserName = model.Email.Trim().ToLowerInvariant(), IsActive = true };
        var result = await users.CreateAsync(user, model.Password);
        if (result.Succeeded) result = await users.AddToRoleAsync(user, "SalesExecutive");
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            await transaction.RollbackAsync(); return View(model);
        }
        await audit.LogAsync("Register", "Authentication", user.Id, newValue: new { user.Name, user.Email, Role = "SalesExecutive" });
        await transaction.CommitAsync();
        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await audit.LogAsync("Logout", "Authentication", users.GetUserId(User));
        await signIn.SignOutAsync(); return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [HttpPost, ValidateAntiForgeryToken, EnableRateLimiting("login")]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var user = await users.GetUserAsync(User);
        if (user == null) return Challenge();
        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors) ModelState.AddModelError("", e.Description);
            await audit.LogAsync("PasswordChange", "Authentication", user.Id, result: "Failed");
            return View(model);
        }
        await signIn.RefreshSignInAsync(user);
        await audit.LogAsync("PasswordChange", "Authentication", user.Id);
        TempData["Success"] = "Your password has been changed.";
        return RedirectToAction("Index", "Dashboard");
    }

    [AllowAnonymous, HttpGet]
    public IActionResult AccessDenied() { Response.StatusCode = 403; return View(); }
}
