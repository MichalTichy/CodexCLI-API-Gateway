using CodexGateway.Logic.Projects;

namespace CodexGateway.Logic.Security;

/// <summary>
/// Establishes the authenticated, project-scoped context consumed by gateway
/// application services. API adapters own credential extraction but cannot
/// construct an authorized context directly.
/// </summary>
public sealed class GatewayAccessService(
    GlobalApiKeyService apiKeys,
    ProjectAccessResolver projects)
{
    public GlobalApiKeyIdentity? Authenticate(string? rawApiKey) =>
        apiKeys.Authenticate(rawApiKey);

    public async Task<GatewayRequestContext?> ResolveAsync(
        GlobalApiKeyIdentity apiKey,
        string? projectId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(apiKey);
        if (projectId is null)
        {
            return new GatewayRequestContext(apiKey.Id, null);
        }

        var access = await projects.ResolveAccessAsync(projectId, apiKey.Id, cancellationToken);
        return access is null
            ? null
            : new GatewayRequestContext(apiKey.Id, access.Project.Id);
    }
}
