using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed class LogoutCodexAccountUseCaseHandler(ICodexControlPlane codex)
    : IRequestHandler<LogoutCodexAccountUseCase>
{
    public Task Handle(
        LogoutCodexAccountUseCase request,
        CancellationToken cancellationToken) =>
        codex.LogoutAsync(cancellationToken);
}
