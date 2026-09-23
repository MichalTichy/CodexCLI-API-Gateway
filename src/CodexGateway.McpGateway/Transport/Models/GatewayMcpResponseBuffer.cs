using CodexGateway.McpGateway.Errors;

namespace CodexGateway.McpGateway.Transport.Models;

internal sealed class GatewayMcpResponseBuffer(long maximumBytes) : MemoryStream
{
    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureCapacity(count);
        base.Write(buffer, offset, count);
    }

    public override Task WriteAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        EnsureCapacity(count);
        return base.WriteAsync(buffer, offset, count, cancellationToken);
    }

    public override ValueTask WriteAsync(
        ReadOnlyMemory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        EnsureCapacity(buffer.Length);
        return base.WriteAsync(buffer, cancellationToken);
    }

    private void EnsureCapacity(int bytesToWrite)
    {
        if (bytesToWrite < 0 || Position > maximumBytes - bytesToWrite)
        {
            throw new GatewayMcpRequestException(
                StatusCodes.Status413PayloadTooLarge,
                "The MCP response exceeds the configured materialized-file limits.");
        }
    }
}
