using CodexGateway.Models;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.Projects;

/// <summary>
/// Selects one enabled project and its one matching API-key grant. Returning
/// no result for missing, disabled, or ambiguous grants preserves the public
/// API's non-disclosure behavior.
/// </summary>
public sealed class ProjectAccessSpecification(string projectId, string apiKeyId)
    : ISpecification<GatewayState, ResolvedProjectAccess?>
{
    public async Task<ResolvedProjectAccess?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = projectId.Trim().ToLowerInvariant();
        var project = await queryable
            .SelectMany(state => state.Projects)
            .Where(candidate =>
                candidate.Enabled &&
                candidate.Id == normalizedProjectId)
            .Select(candidate => candidate)
            .SingleOrDefaultAsync(cancellationToken);
        if (project is null)
        {
            return null;
        }

        var matches = (project.ApiKeyAccess ?? [])
            .OfType<ProjectApiKeyAccess>()
            .Where(access => string.Equals(access.ApiKeyId, apiKeyId, StringComparison.Ordinal))
            .ToArray();
        var result = matches.Length == 1
            ? new ResolvedProjectAccess(project, matches[0])
            : null;
        return result;
    }
}
