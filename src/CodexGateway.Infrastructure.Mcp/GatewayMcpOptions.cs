using System.ComponentModel.DataAnnotations;

namespace CodexGateway.Infrastructure.Mcp;

public sealed class GatewayMcpOptions
{
    public const string SectionName = "McpGateway";

    [Required]
    [Url]
    public string RunnerBaseUrl { get; init; } = "http://host.docker.internal:8080";

    [Range(1, 1_440)]
    public int SessionLifetimeMinutes { get; init; } = 120;
}
