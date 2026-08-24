using System.Text.Json.Serialization;

namespace CodexGateway.Models.McpServers;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "transport")]
[JsonDerivedType(typeof(HttpMcpServerDefinition), "http")]
[JsonDerivedType(typeof(StdioMcpServerDefinition), "stdio")]
public abstract record McpServerDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public McpExecutionMode ExecutionMode { get; init; } = McpExecutionMode.Runner;

    public List<string> EnvironmentVariables { get; init; } = [];

    public List<string> AvailableTools { get; init; } = [];
}
