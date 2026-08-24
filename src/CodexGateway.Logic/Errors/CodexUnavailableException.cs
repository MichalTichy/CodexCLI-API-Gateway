namespace CodexGateway.Logic.Errors;

public sealed class CodexUnavailableException(string message)
    : GatewayException(
        GatewayErrorCategory.Unavailable,
        503,
        "codex_unavailable",
        message);
