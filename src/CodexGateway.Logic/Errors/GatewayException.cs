namespace CodexGateway.Logic.Errors;

public class GatewayException : Exception
{
    public GatewayException(
        GatewayErrorCategory category,
        int statusCode,
        string code,
        string message,
        string? field = null)
        : base(message)
    {
        Category = category;
        StatusCode = statusCode;
        Code = code;
        Field = field;
    }

    public GatewayException(int statusCode, string code, string message, string? field = null)
        : this(CategoryFor(statusCode), statusCode, code, message, field)
    {
    }

    public GatewayErrorCategory Category { get; }

    public int StatusCode { get; }

    public string Code { get; }

    /// <summary>
    /// Gets the protocol-neutral input field associated with the error, when one
    /// exists. An API adapter maps this value to its own wire field name.
    /// </summary>
    public string? Field { get; }

    public static GatewayException InvalidRequest(string message, string code = "invalid_request", string? parameter = null) =>
        new(GatewayErrorCategory.InvalidInput, 400, code, message, parameter);

    public static GatewayException NotFound(string message, string code) =>
        new(GatewayErrorCategory.NotFound, 404, code, message);

    private static GatewayErrorCategory CategoryFor(int statusCode) => statusCode switch
    {
        400 => GatewayErrorCategory.InvalidInput,
        401 => GatewayErrorCategory.Unauthenticated,
        403 => GatewayErrorCategory.Forbidden,
        404 => GatewayErrorCategory.NotFound,
        409 => GatewayErrorCategory.Conflict,
        413 => GatewayErrorCategory.PayloadTooLarge,
        429 => GatewayErrorCategory.CapacityExceeded,
        502 => GatewayErrorCategory.UpstreamFailure,
        503 => GatewayErrorCategory.Unavailable,
        504 => GatewayErrorCategory.Timeout,
        _ => GatewayErrorCategory.Internal
    };

}
