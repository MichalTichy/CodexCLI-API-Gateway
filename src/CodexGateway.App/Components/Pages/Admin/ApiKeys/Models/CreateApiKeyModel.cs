using System.ComponentModel.DataAnnotations;

namespace CodexGateway.App.Components.Pages.Admin.ApiKeys.Models;

internal sealed class CreateApiKeyModel
{
    [Required(ErrorMessage = "Enter a key ID.")]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,63}$", ErrorMessage = "Use 1-64 lowercase letters, numbers, underscores, or hyphens.")]
    public string Id { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter a display name.")]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;
}
