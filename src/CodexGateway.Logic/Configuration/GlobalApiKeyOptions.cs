using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class GlobalApiKeyOptions
{
    /// <summary>
    /// Stable identifier stored with the API key and referenced by project access rules.
    /// It is not the secret presented by clients.
    /// </summary>
    [Required]
    [RegularExpression("^[a-z0-9][a-z0-9_-]{0,63}$")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable label displayed in administration views and logs.</summary>
    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Secret credential accepted from clients using the configured HTTP authentication scheme.
    /// Store it through protected configuration and do not expose it in logs or responses.
    /// </summary>
    [Required]
    public string Key { get; set; } = string.Empty;
}
