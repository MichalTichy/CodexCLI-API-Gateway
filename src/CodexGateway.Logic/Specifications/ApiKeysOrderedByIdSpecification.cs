using CodexGateway.Models;
using CodexGateway.Logic.Security;

namespace CodexGateway.Logic.Specifications;

public sealed class ApiKeysOrderedByIdSpecification
    : ISpecification<GatewayState, IReadOnlyList<GlobalApiKeyIdentity>>
{
    public IReadOnlyList<GlobalApiKeyIdentity> Apply(GatewayState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return source.ApiKeys
            .Select(key => new GlobalApiKeyIdentity(key.Id, key.Name))
            .OrderBy(key => key.Id, StringComparer.Ordinal)
            .ToArray();
    }
}
