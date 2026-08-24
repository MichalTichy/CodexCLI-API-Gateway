namespace CodexGateway.McpGateway.Transport;

public interface IGatewayMcpRequestHandler
{
    Task HandleAsync(HttpContext context, string sessionId, CancellationToken cancellationToken);
}
