using CodexGateway.Models;

namespace CodexGateway.Logic.Specifications;

public sealed class ProjectDefinitionByIdSpecification(string projectId)
    : ISpecification<GatewayState, ProjectDefinition?>
{
    public ProjectDefinition? Apply(GatewayState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.Projects.SingleOrDefault(candidate =>
            string.Equals(candidate.Id, projectId, StringComparison.OrdinalIgnoreCase));
    }
}
