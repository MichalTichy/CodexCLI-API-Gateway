using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.Projects;

public sealed class ProjectsOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ProjectDefinition>>
{
    public async Task<IReadOnlyList<ProjectDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        return await queryable
            .SelectMany(state => state.Projects)
            .OrderBy(project => project.Id)
            .ToListAsync(cancellationToken);
    }
}
