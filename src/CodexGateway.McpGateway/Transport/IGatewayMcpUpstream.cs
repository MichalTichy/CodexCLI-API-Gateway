namespace CodexGateway.McpGateway.Transport;

public interface IGatewayMcpUpstream : IAsyncDisposable
{
    Task ForwardAsync(HttpRequest request, HttpResponse response, CancellationToken cancellationToken);
}
