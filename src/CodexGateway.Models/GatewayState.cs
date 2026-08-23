namespace CodexGateway.Models;

public sealed record GatewayState
{
    public List<GatewayApiKeyDefinition> ApiKeys { get; init; } = [];

    public List<ProjectDefinition> Projects { get; init; } = [];

    public List<McpServerDefinition> McpServers { get; init; } = [];
}
