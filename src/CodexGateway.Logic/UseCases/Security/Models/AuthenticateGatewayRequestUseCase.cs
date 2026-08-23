using MediatR;

namespace CodexGateway.Logic.UseCases.Security;

public sealed record AuthenticateGatewayRequestUseCase(
    string? ApiKey,
    string? ProjectId) : IRequest<GatewayRequestContext?>;
