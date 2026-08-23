using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ProjectsOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ProjectDefinition>>
{
    public Task<IReadOnlyList<ProjectDefinition>?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<ProjectDefinition> result = (queryable.SingleOrDefault()?.Projects ?? [])
            .OrderBy(project => project.Id, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult<IReadOnlyList<ProjectDefinition>?>(result);
    }
}
