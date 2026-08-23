namespace CodexGateway.Logic.McpServers.Models;

public sealed class GatewayMcpSessionLease(
    IReadOnlyDictionary<string, GatewayMcpRunnerConnection> connections,
    Func<ValueTask> dispose)
    : IAsyncDisposable
{
    public IReadOnlyDictionary<string, GatewayMcpRunnerConnection> Connections { get; } = connections;

    public ValueTask DisposeAsync() => dispose();
}
