namespace CodexGateway.Infrastructure.Mcp;

internal interface IGatewayMcpUpstream : IAsyncDisposable
{
    Task ForwardAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken);
}
