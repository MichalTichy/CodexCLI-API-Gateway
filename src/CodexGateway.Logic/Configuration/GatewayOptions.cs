using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public GlobalApiKeyOptions[] ApiKeys { get; set; } = [];

    [Required]
    public string StoragePath { get; set; } = "data";

    public RunLimitOptions Limits { get; set; } = new();

    public FileOptions Files { get; set; } = new();

    public ArtifactOptions Artifacts { get; set; } = new();
}
