using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record LogoutCodexAccountUseCase : IRequest;

public sealed class LogoutCodexAccountUseCaseHandler(ICodexAuthenticationManager codex)
    : IRequestHandler<LogoutCodexAccountUseCase>
{
    public Task Handle(
        LogoutCodexAccountUseCase request,
        CancellationToken cancellationToken) =>
        codex.LogoutAsync(cancellationToken);
}
