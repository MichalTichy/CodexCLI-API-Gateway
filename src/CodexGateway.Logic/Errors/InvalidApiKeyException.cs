namespace CodexGateway.Logic.Errors;

public sealed class InvalidApiKeyException()
    : GatewayException(
        GatewayErrorCategory.Unauthenticated,
        401,
        "invalid_api_key",
        "Incorrect API key provided.");
