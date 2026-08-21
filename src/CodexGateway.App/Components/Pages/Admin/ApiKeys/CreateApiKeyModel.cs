using System.ComponentModel.DataAnnotations;

namespace CodexGateway.App.Components.Admin;

internal sealed class CreateApiKeyModel
{
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,63}$", ErrorMessage = "Use 1-64 lowercase letters, numbers, underscores, or hyphens.")]
    public string Id { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Key { get; set; } = string.Empty;
}
