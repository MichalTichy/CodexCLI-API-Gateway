namespace CodexGateway.Models;

public sealed record HttpMcpServerDefinition : McpServerDefinition
{
    public required string Url { get; init; }

    public Dictionary<string, string> EnvironmentHeaders { get; init; } = [];
}
