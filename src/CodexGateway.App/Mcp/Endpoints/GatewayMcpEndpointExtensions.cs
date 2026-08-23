using CodexGateway.Infrastructure.Mcp;

namespace CodexGateway.App.Mcp.Endpoints;

internal static class GatewayMcpEndpointExtensions
{
    public static IEndpointRouteBuilder MapGatewayMcpEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapMethods(
                "/_internal/mcp/{sessionId}",
                [HttpMethods.Get, HttpMethods.Post, HttpMethods.Delete],
                async (
                    HttpContext context,
                    string sessionId,
                    IGatewayMcpRequestHandler handler,
                    CancellationToken cancellationToken) =>
                {
                    await handler.HandleAsync(context, sessionId, cancellationToken);
                })
            .DisableAntiforgery();
        return endpoints;
    }
}
