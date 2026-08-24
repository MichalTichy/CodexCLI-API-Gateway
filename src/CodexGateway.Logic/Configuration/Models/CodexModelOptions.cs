using System.ComponentModel.DataAnnotations;

namespace CodexGateway.Logic.Configuration.Models;

public sealed class CodexModelOptions
{
    /// <summary>The model identifier accepted by the OpenAI-compatible API and passed to Codex CLI.</summary>
    [Required]
    public string Id { get; set; } = string.Empty;

    /// <summary>The human-readable model name returned by the model catalog endpoint.</summary>
    [Required]
    public string Name { get; set; } = string.Empty;

    /// <summary>The reasoning-effort values clients may request for this model.</summary>
    [MinLength(1)]
    public string[] SupportedReasoningEfforts { get; set; } = [];

    /// <summary>The reasoning effort used when a request does not specify one.</summary>
    [Required]
    public string DefaultReasoningEffort { get; set; } = string.Empty;
}
