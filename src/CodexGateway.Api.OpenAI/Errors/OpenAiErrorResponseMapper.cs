using CodexGateway.Logic.Errors;

namespace CodexGateway.Api.OpenAI.Errors;

public static class OpenAiErrorResponseMapper
{
    public static OpenAiError Map(GatewayException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var code = exception.Category == GatewayErrorCategory.CapacityExceeded &&
                   string.Equals(exception.Code, "queue_full", StringComparison.Ordinal)
            ? "rate_limit_exceeded"
            : exception.Code;
        return new OpenAiError(
            exception.Message,
            Type(exception.Category),
            exception.Field,
            code);
    }

    public static object Response(GatewayException exception) => Response(Map(exception));

    public static object Response(OpenAiError error) => new
    {
        error = new
        {
            message = error.Message,
            type = error.Type,
            param = error.Parameter,
            code = error.Code
        }
    };

    private static string Type(GatewayErrorCategory category) => category switch
    {
        GatewayErrorCategory.CapacityExceeded => "rate_limit_error",
        GatewayErrorCategory.Timeout or
        GatewayErrorCategory.Unavailable or
        GatewayErrorCategory.UpstreamFailure or
        GatewayErrorCategory.Internal => "server_error",
        _ => "invalid_request_error"
    };
}
