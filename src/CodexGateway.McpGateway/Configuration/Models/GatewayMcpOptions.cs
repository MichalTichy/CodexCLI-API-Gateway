using System.ComponentModel.DataAnnotations;

namespace CodexGateway.McpGateway.Configuration.Models;

public sealed class GatewayMcpOptions
{
    public const string SectionName = "McpGateway";

    /// <summary>
    /// Base address at which run containers can reach this gateway.
    /// It is used to build short-lived internal MCP session URLs and is not an upstream MCP URL.
    /// </summary>
    [Required]
    [Url]
    public string RunnerBaseUrl { get; init; } = "http://host.docker.internal:8080";

    /// <summary>
    /// Maximum lifetime of an internal runner-to-gateway MCP session.
    /// A session is normally removed earlier when its run finishes.
    /// </summary>
    [Range(1, 1_440)]
    public int SessionLifetimeMinutes { get; init; } = 120;
}
