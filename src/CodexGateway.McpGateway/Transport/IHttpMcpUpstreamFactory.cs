using CodexGateway.Models.McpServers;

namespace CodexGateway.McpGateway.Transport;

public interface IHttpMcpUpstreamFactory
{
    IGatewayMcpUpstream Create(HttpMcpServerDefinition definition);
}
