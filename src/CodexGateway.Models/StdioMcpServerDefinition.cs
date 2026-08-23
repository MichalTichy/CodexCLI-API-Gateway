namespace CodexGateway.Models;

public sealed record StdioMcpServerDefinition : McpServerDefinition
{
    public required string Command { get; init; }

    public List<string> Arguments { get; init; } = [];

}
