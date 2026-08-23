namespace CodexGateway.Logic.McpServers;

public sealed record GatewayMcpRunnerConnection(
    string Url,
    string BearerTokenEnvironmentVariable,
    string BearerToken);
