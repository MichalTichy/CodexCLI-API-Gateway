using CodexGateway.McpGateway.Transport;
using CodexGateway.Models.McpServers;
using Microsoft.Extensions.Logging;

namespace CodexGateway.McpGateway.Stdio.Transport;

public sealed class StdioMcpUpstreamFactory(ILogger<StdioMcpUpstreamFactory> logger)
    : IStdioMcpUpstreamFactory
{
    public async Task<IGatewayMcpUpstream> StartAsync(
        StdioMcpServerDefinition definition,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspacePath);
        return await LocalStdioGatewayMcpUpstream.StartAsync(
            definition,
            workspacePath,
            GetEnvironmentValues(definition.EnvironmentVariables),
            logger,
            cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> GetEnvironmentValues(
        IEnumerable<string>? names)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in (names ?? []).Where(name => !string.IsNullOrWhiteSpace(name))
                     .Distinct(StringComparer.Ordinal))
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (value is not null)
            {
                values.Add(name, value);
            }
        }

        return values;
    }
}
