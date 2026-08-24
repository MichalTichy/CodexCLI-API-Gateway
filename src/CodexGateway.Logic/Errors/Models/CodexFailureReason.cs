namespace CodexGateway.Logic.Errors.Models;

public enum CodexFailureReason
{
    TurnFailed,
    MalformedOutput,
    IncompleteTurn,
    MissingAgentResponse,
    InvalidStructuredOutput
}
