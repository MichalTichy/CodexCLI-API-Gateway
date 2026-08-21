using CodexGateway.Models;

namespace CodexGateway.Logic.Specifications;

public sealed class ProjectsOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<ProjectDefinition>>
{
    public IReadOnlyList<ProjectDefinition> Apply(GatewayState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Projects
            .OrderBy(project => project.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
