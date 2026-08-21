namespace CodexGateway.Infrastructure.Mcp;

internal sealed class GatewayMcpRequestException(
    int statusCode,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
