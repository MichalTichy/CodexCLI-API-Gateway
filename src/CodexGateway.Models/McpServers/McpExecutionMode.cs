namespace CodexGateway.Models.McpServers;

public enum McpExecutionMode
{
    /// <summary>
    /// Starts the MCP server inside the isolated Codex runner for each request.
    /// </summary>
    Runner,

    /// <summary>
    /// Connects the Codex runner to an MCP session managed by the gateway host.
    /// </summary>
    Gateway
}
