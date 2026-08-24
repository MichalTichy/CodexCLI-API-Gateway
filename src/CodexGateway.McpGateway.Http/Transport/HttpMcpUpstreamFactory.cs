using CodexGateway.McpGateway.Transport;
using CodexGateway.Models.McpServers;

namespace CodexGateway.McpGateway.Http.Transport;

public sealed class HttpMcpUpstreamFactory(IHttpClientFactory httpClientFactory)
    : IHttpMcpUpstreamFactory
{
    internal const string HttpClientName = "McpGateway.Http";

    public IGatewayMcpUpstream Create(HttpMcpServerDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return new HttpGatewayMcpUpstream(
            httpClientFactory.CreateClient(HttpClientName),
            new Uri(definition.Url, UriKind.Absolute),
            GetEnvironmentHeaders(definition));
    }

    private static IReadOnlyDictionary<string, string> GetEnvironmentHeaders(
        HttpMcpServerDefinition definition)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (headerName, environmentVariable) in definition.EnvironmentHeaders)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"Environment variable '{environmentVariable}' for HTTP MCP server '{definition.Id}' is unavailable.");
            }

            values.Add(headerName, value);
        }

        return values;
    }
}
