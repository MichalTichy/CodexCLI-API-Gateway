using CodexGateway.Models;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeyDefinitionsSpecification
    : ISpecification<GatewayState, IReadOnlyList<GatewayApiKeyDefinition>>
{
    public IReadOnlyList<GatewayApiKeyDefinition> Apply(GatewayState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ApiKeys;
    }
}
