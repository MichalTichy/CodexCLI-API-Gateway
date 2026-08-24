using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration.Models;

public sealed class GatewayOptions
{
    /// <summary>The configuration section bound to these gateway settings.</summary>
    public const string SectionName = "Gateway";

    /// <summary>
    /// Root directory for project artifacts, temporary uploads, and per-run workspaces.
    /// Relative paths are resolved from the application content root.
    /// </summary>
    [Required]
    public string StoragePath { get; set; } = "data";

    /// <summary>Controls how many Codex runs may execute or wait, and how long each run may take.</summary>
    public RunLimitOptions Limits { get; set; } = new();

    /// <summary>Controls accepted uploads and the lifetime of uploads that are not attached to a project.</summary>
    public FileOptions Files { get; set; } = new();

    /// <summary>Limits the files and bytes that Codex may read from or write to an artifact workspace.</summary>
    public ArtifactOptions Artifacts { get; set; } = new();
}
