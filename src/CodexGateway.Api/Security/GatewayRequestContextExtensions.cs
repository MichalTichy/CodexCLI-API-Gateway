using CodexGateway.Logic;
using Microsoft.AspNetCore.Http;

namespace CodexGateway.Api;

public static class GatewayRequestContextExtensions
{
    public static GatewayRequestContext GetGatewayRequestContext(this HttpContext context) =>
        context.Features.Get<GatewayRequestContext>()
        ?? throw new InvalidOperationException("The Gateway API request identity has not been established.");
}
