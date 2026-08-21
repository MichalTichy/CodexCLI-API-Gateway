namespace CodexGateway.Models;

public enum McpExecutionMode
{
    // The zero value preserves the behavior of persisted configurations created
    // before gateway-hosted MCP sessions were introduced.
    Runner,

    Gateway
}
