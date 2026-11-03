using AcxiomCRM.Data;
using AcxiomCRM.Models;
using AcxiomCRM.Services;
using AcxiomCRM.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AcxiomCRM.Controllers;

[Authorize(Roles = "Admin")]
public class UsersController(UserManager<ApplicationUser> users, AuditService audit, ApplicationDbContext db) : Controller
{
    private static readonly string[] Roles = ["Admin", "Manager", "SalesExecutive"];

    public async Task<IActionResult> Index(string search = "", int page = 1)
    {
        search = (search ?? "").Trim();
        var query = users.Users.AsNoTracking();
        if (search.Length > 0) query = query.Where(x => x.Name.Contains(search) || (x.Email != null && x.Email.Contains(search)));
        var total = await query.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / 15.0)); page = Math.Clamp(page, 1, pages);
        var list = new List<UserRow>();
        foreach (var u in await query.OrderBy(x => x.Name).Skip((page - 1) * 15).Take(15).ToListAsync())
            list.Add(new(u.Id, u.Name, u.Email ?? "", string.Join(", ", await users.GetRolesAsync(u)), u.IsActive, u.LockoutEnd));
        return View(new UserListViewModel { Users = list, Search = search, Page = page, TotalPages = pages, Total = total });
    }

    [HttpGet]
    public IActionResult Create() => View("Edit", new UserEditViewModel());

    [HttpGet]
    public async Task<IActionResult> Edit(string id)
    {
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        return View(new UserEditViewModel { Id = user.Id, Name = user.Name, Email = user.Email ?? "", Role = (await users.GetRolesAsync(user)).FirstOrDefault() ?? "SalesExecutive", IsActive = user.IsActive });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(UserEditViewModel model)
    {
        if (!Roles.Contains(model.Role)) ModelState.AddModelError(nameof(model.Role), "Choose a valid role.");
        if (string.IsNullOrWhiteSpace(model.Name)) ModelState.AddModelError(nameof(model.Name), "Full name is required.");
        if (string.IsNullOrEmpty(model.Id) && string.IsNullOrEmpty(model.Password)) ModelState.AddModelError(nameof(model.Password), "An initial password is required.");
        if (!ModelState.IsValid) return View("Edit", model);
        var create = string.IsNullOrEmpty(model.Id);
        var user = create ? new ApplicationUser() : await users.FindByIdAsync(model.Id!);
        if (user == null) return NotFound();
        var oldRoles = create ? Array.Empty<string>() : (await users.GetRolesAsync(user)).ToArray();
        // Keep the last-administrator guard and the role/status write in one transaction.
        await using var transaction = await db.Database.BeginTransactionAsync();
        if (!create && oldRoles.Contains("Admin") && user.IsActive && (!model.IsActive || model.Role != "Admin") && (await users.GetUsersInRoleAsync("Admin")).Count(x => x.IsActive) <= 1)
        {
            ModelState.AddModelError("", "At least one active administrator must remain."); return View("Edit", model);
        }
        var before = new { user.Name, user.Email, user.IsActive, Role = string.Join(",", oldRoles) };
        user.Name = model.Name.Trim(); user.Email = model.Email.Trim().ToLowerInvariant(); user.UserName = user.Email; user.IsActive = model.IsActive;
        var result = create ? await users.CreateAsync(user, model.Password!) : await users.UpdateAsync(user);
        if (result.Succeeded && !oldRoles.SequenceEqual(new[] { model.Role }))
        {
            if (oldRoles.Length > 0) result = await users.RemoveFromRolesAsync(user, oldRoles);
            if (result.Succeeded) result = await users.AddToRoleAsync(user, model.Role);
        }
        if (result.Succeeded && !create) result = await users.UpdateSecurityStampAsync(user);
        if (!result.Succeeded)
        {
            await transaction.RollbackAsync(); foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); return View("Edit", model);
        }
        await audit.LogAsync(create ? "Create" : "Update", "Users", user.Id, create ? null : before, new { user.Name, user.Email, user.IsActive, model.Role });
        if (!create && !oldRoles.SequenceEqual(new[] { model.Role })) await audit.LogAsync("RoleChange", "Users", user.Id, new { Role = string.Join(",", oldRoles) }, new { model.Role });
        if (!create && before.IsActive != user.IsActive) await audit.LogAsync(user.IsActive ? "Activate" : "Deactivate", "Users", user.Id);
        await transaction.CommitAsync();
        TempData["Success"] = create ? "User created." : "User updated. Existing sessions will be revalidated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> ResetPassword(string id)
    {
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        return View(new ResetPasswordViewModel { Id = id, Name = user.Name });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        var user = await users.FindByIdAsync(model.Id); if (user == null) return NotFound();
        model.Name = user.Name;
        if (!ModelState.IsValid) return View(model);
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var result = await users.ResetPasswordAsync(user, token, model.NewPassword);
        if (!result.Succeeded) { foreach (var e in result.Errors) ModelState.AddModelError("", e.Description); return View(model); }
        await users.UpdateSecurityStampAsync(user);
        await audit.LogAsync("PasswordReset", "Users", user.Id);
        TempData["Success"] = "Password reset. Existing sessions have been revoked.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Unlock(string id)
    {
        var user = await users.FindByIdAsync(id); if (user == null) return NotFound();
        var result = await users.SetLockoutEndDateAsync(user, null);
        if (result.Succeeded) result = await users.ResetAccessFailedCountAsync(user);
        if (!result.Succeeded) { TempData["Error"] = "The account could not be unlocked."; return RedirectToAction(nameof(Index)); }
        await audit.LogAsync("Unlock", "Users", user.Id);
        TempData["Success"] = "Account unlocked."; return RedirectToAction(nameof(Index));
    }
}
