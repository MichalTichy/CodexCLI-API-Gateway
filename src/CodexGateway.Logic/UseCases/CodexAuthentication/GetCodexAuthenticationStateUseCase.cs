using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record GetCodexAuthenticationStateUseCase
    : IRequest<CodexAuthenticationState>;

public sealed class GetCodexAuthenticationStateUseCaseHandler(ICodexControlPlane codex)
    : IRequestHandler<GetCodexAuthenticationStateUseCase, CodexAuthenticationState>
{
    public async Task<CodexAuthenticationState> Handle(
        GetCodexAuthenticationStateUseCase request,
        CancellationToken cancellationToken)
    {
        var accountTask = codex.GetAccountAsync(cancellationToken);
        var loginTask = codex.GetDeviceLoginAsync(cancellationToken);
        await Task.WhenAll(accountTask, loginTask);
        return new CodexAuthenticationState(await accountTask, await loginTask);
    }
}
