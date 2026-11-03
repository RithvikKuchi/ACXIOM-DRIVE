using System.ComponentModel.DataAnnotations;

namespace AcxiomCRM.ViewModels;

public class LoginViewModel
{
    [Required, StringLength(254), Display(Name = "Email or username")] public string Email { get; set; } = "";
    [Required, DataType(DataType.Password), StringLength(128)] public string Password { get; set; } = "";
    [Display(Name = "Remember me")] public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}
public class RegisterViewModel
{
    [Required, StringLength(100), Display(Name = "Full name")] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password)] public string Password { get; set; } = "";
    [Required, Compare("Password"), DataType(DataType.Password), Display(Name = "Confirm password")] public string ConfirmPassword { get; set; } = "";
}
public class ChangePasswordViewModel
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")] public string CurrentPassword { get; set; } = "";
    [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, Compare("NewPassword"), DataType(DataType.Password), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
}
public class UserEditViewModel
{
    public string? Id { get; set; }
    [Required, StringLength(100), Display(Name = "Full name")] public string Name { get; set; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; set; } = "";
    [Required] public string Role { get; set; } = "SalesExecutive";
    public bool IsActive { get; set; } = true;
    [DataType(DataType.Password), StringLength(128, MinimumLength = 10), Display(Name = "Initial password")] public string? Password { get; set; }
}
public class ResetPasswordViewModel
{
    [Required] public string Id { get; set; } = "";
    public string? Name { get; set; }
    [Required, StringLength(128, MinimumLength = 10), DataType(DataType.Password), Display(Name = "New password")] public string NewPassword { get; set; } = "";
    [Required, Compare("NewPassword"), DataType(DataType.Password), Display(Name = "Confirm new password")] public string ConfirmPassword { get; set; } = "";
}
public record UserRow(string Id, string Name, string Email, string Role, bool IsActive, DateTimeOffset? LockoutEnd);
public class UserListViewModel
{
    public List<UserRow> Users { get; set; } = [];
    public string Search { get; set; } = "";
    public int Page { get; set; }
    public int TotalPages { get; set; }
    public int Total { get; set; }
}
