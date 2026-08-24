using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record CancelCodexDeviceLoginUseCase : IRequest;

public sealed class CancelCodexDeviceLoginUseCaseHandler(ICodexAuthenticationManager codex)
    : IRequestHandler<CancelCodexDeviceLoginUseCase>
{
    public Task Handle(
        CancelCodexDeviceLoginUseCase request,
        CancellationToken cancellationToken) =>
        codex.CancelDeviceLoginAsync(cancellationToken);
}
