using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record StartCodexDeviceLoginUseCase : IRequest<DeviceLogin>;

public sealed class StartCodexDeviceLoginUseCaseHandler(ICodexAuthenticationManager codex)
    : IRequestHandler<StartCodexDeviceLoginUseCase, DeviceLogin>
{
    public Task<DeviceLogin> Handle(
        StartCodexDeviceLoginUseCase request,
        CancellationToken cancellationToken) =>
        codex.StartDeviceLoginAsync(cancellationToken);
}
