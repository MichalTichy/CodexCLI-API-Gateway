using System.Text.Json;

namespace CodexGateway.McpGateway.Transport.Models;

public static class GatewayMcpRequestBody
{
    private const int MaximumBytes = 1_048_576;

    public static async Task<byte[]> ReadAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength is > MaximumBytes)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "The MCP request body is too large.");
        }

        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await request.Body.ReadAsync(chunk, cancellationToken)) != 0)
        {
            if (buffer.Length + read > MaximumBytes)
            {
                throw new GatewayMcpRequestException(
                    StatusCodes.Status413PayloadTooLarge,
                    "The MCP request body is too large.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return buffer.ToArray();
    }

    public static bool ContainsMethod(byte[] body, string method)
    {
        ArgumentNullException.ThrowIfNull(body);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind switch
            {
                JsonValueKind.Object => IsMethod(document.RootElement, method),
                JsonValueKind.Array => document.RootElement.EnumerateArray()
                    .Any(item => item.ValueKind == JsonValueKind.Object && IsMethod(item, method)),
                _ => false
            };
        }
        catch (JsonException exception)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status400BadRequest,
                "The MCP request is not valid JSON-RPC.",
                exception);
        }
    }

    private static bool IsMethod(JsonElement request, string method) =>
        request.TryGetProperty("method", out var value) &&
        value.ValueKind == JsonValueKind.String &&
        string.Equals(value.GetString(), method, StringComparison.Ordinal);
}
