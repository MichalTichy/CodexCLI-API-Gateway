using CodexGateway.Logic.Errors;

namespace CodexGateway.Tests;

public sealed class CodexFailedExceptionTests
{
    [Theory]
    [InlineData(CodexFailureReason.TurnFailed, "Codex reported that the turn failed.")]
    [InlineData(CodexFailureReason.MalformedOutput, "Codex produced malformed event output.")]
    [InlineData(CodexFailureReason.IncompleteTurn, "Codex exited before completing the turn.")]
    [InlineData(CodexFailureReason.MissingAgentResponse, "Codex completed without an agent response.")]
    [InlineData(CodexFailureReason.InvalidStructuredOutput, "Codex returned invalid JSON for the requested structured output.")]
    public void Failure_reason_has_a_safe_public_message(CodexFailureReason reason, string message)
    {
        var exception = new CodexFailedException(reason);

        Assert.Equal(reason, exception.Reason);
        Assert.Equal(message, exception.Message);
        Assert.Equal("codex_failed", exception.Code);
        Assert.Equal(502, exception.StatusCode);
    }
}
