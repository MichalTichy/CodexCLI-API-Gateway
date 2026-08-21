using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed class StartCodexDeviceLoginUseCaseHandler(ICodexControlPlane codex)
    : IRequestHandler<StartCodexDeviceLoginUseCase, DeviceLogin>
{
    public Task<DeviceLogin> Handle(
        StartCodexDeviceLoginUseCase request,
        CancellationToken cancellationToken) =>
        codex.StartDeviceLoginAsync(cancellationToken);
}
