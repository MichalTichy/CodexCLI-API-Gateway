namespace CodexGateway.Logic;

/// <summary>
/// Identifies the authenticated gateway caller and its optional project scope.
/// API adapters create this value without exposing transport-specific request details
/// to the application layer.
/// </summary>
public sealed record GatewayRequestContext
{
    internal GatewayRequestContext(string apiKeyId, string? projectId)
    {
        ApiKeyId = apiKeyId;
        ProjectId = projectId;
    }

    public string ApiKeyId { get; }

    public string? ProjectId { get; }
}
