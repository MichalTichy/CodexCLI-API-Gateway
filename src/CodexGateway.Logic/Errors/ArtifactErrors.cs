namespace CodexGateway.Logic.Errors;

public static class ArtifactErrors
{
    public static GatewayException LimitExceeded() => new(
        GatewayErrorCategory.PayloadTooLarge,
        413,
        "artifact_limit_exceeded",
        "The run artifacts exceed the configured file-count or size limit.");
}
