namespace CodexGateway.Infrastructure.Mcp.Errors;

internal sealed class GatewayMcpRequestException(
    int statusCode,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
