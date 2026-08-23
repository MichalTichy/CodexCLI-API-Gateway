using System.ComponentModel.DataAnnotations;

namespace CodexGateway.App.Components.Pages.Admin.Projects.Models;

internal sealed class CreateProjectModel
{
    [Required]
    [RegularExpression(
        "^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$",
        ErrorMessage = "Use 2–64 lowercase letters, numbers, or hyphens.")]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
}
