namespace CodexGateway.Logic.Errors;

public sealed class RunTimedOutException()
    : GatewayException(
        GatewayErrorCategory.Timeout,
        504,
        "run_timeout",
        "The Codex run exceeded the configured timeout.");
