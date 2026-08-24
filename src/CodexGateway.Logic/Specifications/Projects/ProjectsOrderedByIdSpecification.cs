using CodexGateway.Models;
using Marten;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications.Projects;

public sealed class ProjectsOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ProjectDefinition>>
{
    public async Task<IReadOnlyList<ProjectDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        var projects = await queryable
            .SelectMany(state => state.Projects)
            .Select(project => project)
            .ToListAsync(cancellationToken);
        return projects
            .OrderBy(project => project.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
