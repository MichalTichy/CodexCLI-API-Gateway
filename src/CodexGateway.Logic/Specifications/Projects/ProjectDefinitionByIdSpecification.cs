using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.Projects;

public sealed class ProjectDefinitionByIdSpecification(string projectId)
    : ISpecification<GatewayState, ProjectDefinition?>
{
    public Task<ProjectDefinition?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        var normalizedProjectId = projectId.Trim().ToLowerInvariant();
        return queryable
            .SelectMany(state => state.Projects)
            .Where(candidate => candidate.Id == normalizedProjectId)
            .SingleOrDefaultAsync(cancellationToken);
    }
}
