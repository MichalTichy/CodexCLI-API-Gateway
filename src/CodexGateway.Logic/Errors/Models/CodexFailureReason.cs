namespace CodexGateway.Logic.Errors;

public enum CodexFailureReason
{
    TurnFailed,
    MalformedOutput,
    IncompleteTurn,
    MissingAgentResponse,
    InvalidStructuredOutput
}
