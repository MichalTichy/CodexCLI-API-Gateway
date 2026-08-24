using System.ComponentModel.DataAnnotations;

namespace CodexGateway.App.Admin.Authentication.Models;

public sealed class AdminUiOptions
{
    public const string SectionName = "AdminUi";

    public bool Enabled { get; set; } = true;

    [Required]
    public string Username { get; set; } = "admin";

    [Required]
    public string Password { get; set; } = "internal-password";
}
