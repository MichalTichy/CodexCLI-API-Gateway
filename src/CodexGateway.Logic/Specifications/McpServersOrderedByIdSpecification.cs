using CodexGateway.Models;

namespace CodexGateway.Logic.Specifications;

public sealed class McpServersOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<McpServerDefinition>>
{
    public IReadOnlyList<McpServerDefinition> Apply(GatewayState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.McpServers
            .OrderBy(server => server.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
