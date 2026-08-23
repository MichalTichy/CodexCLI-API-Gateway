using CodexGateway.Models;
using Shared.Infrastructure.Persistence.Specifications;

namespace CodexGateway.Logic.Specifications;

public sealed class ProjectDefinitionByIdSpecification(string projectId)
    : ISpecification<GatewayState, ProjectDefinition?>
{
    public Task<ProjectDefinition?> ApplyAsync(
        IQueryable<GatewayState> queryable,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = queryable.SingleOrDefault()?.Projects.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, projectId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(result);
    }
}
