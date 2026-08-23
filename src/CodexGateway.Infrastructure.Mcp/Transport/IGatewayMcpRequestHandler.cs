namespace CodexGateway.Infrastructure.Mcp.Transport;

public interface IGatewayMcpRequestHandler
{
    Task HandleAsync(HttpContext context, string sessionId, CancellationToken cancellationToken);
}
