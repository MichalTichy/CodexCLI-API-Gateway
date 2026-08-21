namespace CodexGateway.Logic.Errors;

public sealed class CodexFailedException(CodexFailureReason reason)
    : GatewayException(
        GatewayErrorCategory.UpstreamFailure,
        502,
        "codex_failed",
        MessageFor(reason))
{
    public CodexFailureReason Reason { get; } = reason;

    private static string MessageFor(CodexFailureReason reason) => reason switch
    {
        CodexFailureReason.TurnFailed => "Codex reported that the turn failed.",
        CodexFailureReason.MalformedOutput => "Codex produced malformed event output.",
        CodexFailureReason.IncompleteTurn => "Codex exited before completing the turn.",
        CodexFailureReason.MissingAgentResponse => "Codex completed without an agent response.",
        CodexFailureReason.InvalidStructuredOutput => "Codex returned invalid JSON for the requested structured output.",
        _ => "Codex could not complete the request."
    };
}
