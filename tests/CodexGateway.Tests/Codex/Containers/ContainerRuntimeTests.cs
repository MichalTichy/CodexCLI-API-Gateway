using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.McpServers;

namespace CodexGateway.Tests.Codex.Containers;

public sealed class ContainerRuntimeTests
{
    [Fact]
    public void Environment_snapshot_includes_scoped_gateway_mcp_tokens()
    {
        var connection = new GatewayMcpRunnerConnection(
            "http://gateway.test/_internal/mcp/session",
            "CODEX_GATEWAY_MCP_TEST_TOKEN",
            "scoped-token-value");

        var environment = ContainerRuntime.GetEnvironmentSnapshot(
            new Dictionary<string, GatewayMcpRunnerConnection>
            {
                ["test-mcp"] = connection
            });

        Assert.Equal("scoped-token-value", environment["CODEX_GATEWAY_MCP_TEST_TOKEN"]);
    }
}
