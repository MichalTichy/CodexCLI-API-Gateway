namespace CodexGateway.Infrastructure.Mcp.Transport;

internal interface IGatewayMcpUpstream : IAsyncDisposable
{
    Task ForwardAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken);
}
