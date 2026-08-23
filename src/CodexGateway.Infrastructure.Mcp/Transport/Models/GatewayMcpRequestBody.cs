namespace CodexGateway.Infrastructure.Mcp.Transport.Models;

internal static class GatewayMcpRequestBody
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
}
