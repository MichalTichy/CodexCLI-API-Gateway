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
        endpoints.MapGet(
                "/_internal/mcp/artifacts/{token}",
                async (
                    HttpContext context,
                    string token,
                    IGatewayMcpRequestHandler handler,
                    CancellationToken cancellationToken) =>
                {
                    await handler.HandleArtifactAsync(context, token, cancellationToken);
                })
            .DisableAntiforgery();
        return endpoints;
    }
}
