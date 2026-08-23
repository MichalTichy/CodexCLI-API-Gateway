using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

/// <summary>
/// Selects one enabled project and its one matching API-key grant. Returning
/// no result for missing, disabled, or ambiguous grants preserves the public
/// API's non-disclosure behavior.
/// </summary>
public sealed class ProjectAccessSpecification(string projectId, string apiKeyId)
    : ISpecification<GatewayState, ResolvedProjectAccess?>
{
    public Task<ResolvedProjectAccess?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = queryable.SingleOrDefault();
        if (source is null)
        {
            return Task.FromResult<ResolvedProjectAccess?>(null);
        }

        var project = source.Projects.SingleOrDefault(candidate =>
            candidate.Enabled &&
            string.Equals(candidate.Id, projectId, StringComparison.OrdinalIgnoreCase));
        if (project is null)
        {
            return Task.FromResult<ResolvedProjectAccess?>(null);
        }

        var matches = (project.ApiKeyAccess ?? [])
            .OfType<ProjectApiKeyAccess>()
            .Where(access => string.Equals(access.ApiKeyId, apiKeyId, StringComparison.Ordinal))
            .ToArray();
        var result = matches.Length == 1
            ? new ResolvedProjectAccess(project, matches[0])
            : null;
        return Task.FromResult(result);
    }
}
