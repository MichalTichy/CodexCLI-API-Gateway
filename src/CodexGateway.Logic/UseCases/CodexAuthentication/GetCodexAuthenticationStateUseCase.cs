using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record GetCodexAuthenticationStateUseCase
    : IRequest<CodexAuthenticationState>;

public sealed class GetCodexAuthenticationStateUseCaseHandler(ICodexAuthenticationManager codex)
    : IRequestHandler<GetCodexAuthenticationStateUseCase, CodexAuthenticationState>
{
    public async Task<CodexAuthenticationState> Handle(
        GetCodexAuthenticationStateUseCase request,
        CancellationToken cancellationToken)
    {
        var login = await codex.GetDeviceLoginAsync(cancellationToken);
        var account = login is { Status: DeviceLoginStatus.Pending }
            ? new CodexAccountStatus(false, null, null)
            : await codex.GetAccountAsync(cancellationToken);
        return new CodexAuthenticationState(account, login);
    }
}
