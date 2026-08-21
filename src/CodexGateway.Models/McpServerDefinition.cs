namespace CodexGateway.Models;

public sealed record McpServerDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public McpExecutionMode ExecutionMode { get; init; } = McpExecutionMode.Runner;

    public McpTransport Transport { get; init; }

    public string? Url { get; init; }

    public string? Command { get; init; }

    public List<string> Arguments { get; init; } = [];

    public string? BearerTokenEnvironmentVariable { get; init; }

    public List<string> EnvironmentVariables { get; init; } = [];

    public List<string> AvailableTools { get; init; } = [];
}
