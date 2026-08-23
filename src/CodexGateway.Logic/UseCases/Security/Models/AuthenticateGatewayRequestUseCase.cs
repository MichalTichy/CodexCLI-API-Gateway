using MediatR;

namespace CodexGateway.Logic.UseCases.Security.Models;

public sealed record AuthenticateGatewayRequestUseCase(
    string? ApiKey,
    string? ProjectId) : IRequest<GatewayRequestContext?>;
