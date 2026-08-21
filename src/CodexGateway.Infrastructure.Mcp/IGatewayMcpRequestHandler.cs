namespace CodexGateway.Infrastructure.Mcp;

public interface IGatewayMcpRequestHandler
{
    Task HandleAsync(HttpContext context, string sessionId, CancellationToken cancellationToken);
}
