using System.Text.Json;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;

namespace CodexGateway.Logic.Storage;

public interface IGatewayConfigurationRepository
{
    Task<TResult> QueryAsync<TResult>(
        ISpecification<GatewayState, TResult> specification,
        CancellationToken cancellationToken = default);

    Task<GatewayState> UpdateAsync(
        Func<GatewayState, GatewayState> update,
        CancellationToken cancellationToken = default);
}
