namespace CodexGateway.McpGateway.Errors;

public sealed class GatewayMcpRequestException(
    int statusCode,
    string message,
    Exception? innerException = null)
    : Exception(message, innerException)
{
    public int StatusCode { get; } = statusCode;
}
