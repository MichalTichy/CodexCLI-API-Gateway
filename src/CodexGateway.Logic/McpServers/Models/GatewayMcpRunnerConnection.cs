namespace CodexGateway.Logic.McpServers.Models;

public sealed record GatewayMcpRunnerConnection(
    string Url,
    string SessionTokenEnvironmentVariable,
    string SessionToken);
