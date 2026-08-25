using System.ComponentModel.DataAnnotations;

namespace SecureQrPortal.ViewModels;

public sealed class LoginVm { [Required,EmailAddress] public string Email { get; set; }=""; [Required,DataType(DataType.Password)] public string Password { get; set; }=""; public bool RememberMe { get; set; } }
public sealed class SetupVm { [Required] public string DisplayName { get; set; }="System Administrator"; [Required,EmailAddress] public string Email { get; set; }=""; [Required,DataType(DataType.Password),MinLength(10)] public string Password { get; set; }=""; [Compare(nameof(Password)),DataType(DataType.Password)] public string ConfirmPassword { get; set; }=""; }
public sealed class ChangePasswordVm { [Required,DataType(DataType.Password)] public string CurrentPassword { get; set; }=""; [Required,DataType(DataType.Password),MinLength(10)] public string NewPassword { get; set; }=""; [Compare(nameof(NewPassword)),DataType(DataType.Password)] public string ConfirmPassword { get; set; }=""; }
