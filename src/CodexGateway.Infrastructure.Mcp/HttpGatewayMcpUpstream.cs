using System.Net.Http.Headers;

namespace CodexGateway.Infrastructure.Mcp;

internal sealed class HttpGatewayMcpUpstream(
    HttpClient client,
    Uri endpoint,
    IReadOnlyDictionary<string, string> environmentHeaders)
    : IGatewayMcpUpstream
{
    private static readonly string[] ForwardedHeaders =
    [
        "Accept",
        "Last-Event-ID",
        "Mcp-Protocol-Version",
        "Mcp-Session-Id"
    ];

    private static readonly string[] ResponseHeaders =
    [
        "Mcp-Protocol-Version",
        "Mcp-Session-Id"
    ];

    public async Task ForwardAsync(
        HttpRequest request,
        HttpResponse response,
        CancellationToken cancellationToken)
    {
        using var upstreamRequest = new HttpRequestMessage(new HttpMethod(request.Method), endpoint);
        foreach (var headerName in ForwardedHeaders)
        {
            if (request.Headers.TryGetValue(headerName, out var values))
            {
                upstreamRequest.Headers.TryAddWithoutValidation(headerName, values.ToArray());
            }
        }

        if (RequestCanHaveBody(request.Method))
        {
            var body = await GatewayMcpRequestBody.ReadAsync(request, cancellationToken);
            upstreamRequest.Content = new ByteArrayContent(body);
            if (!string.IsNullOrWhiteSpace(request.ContentType))
            {
                upstreamRequest.Content.Headers.ContentType =
                    MediaTypeHeaderValue.Parse(request.ContentType);
            }
        }

        foreach (var (headerName, value) in environmentHeaders)
        {
            if (!upstreamRequest.Headers.TryAddWithoutValidation(headerName, value))
            {
                throw new InvalidOperationException(
                    $"HTTP MCP header '{headerName}' cannot be added to the upstream request.");
            }
        }

        using var upstreamResponse = await client.SendAsync(
            upstreamRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.StatusCode = (int)upstreamResponse.StatusCode;
        response.ContentType = upstreamResponse.Content.Headers.ContentType?.ToString()
            ?? "application/json";
        foreach (var headerName in ResponseHeaders)
        {
            if (upstreamResponse.Headers.TryGetValues(headerName, out var values))
            {
                response.Headers[headerName] = values.ToArray();
            }
        }

        await upstreamResponse.Content.CopyToAsync(response.Body, cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static bool RequestCanHaveBody(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method);
}
