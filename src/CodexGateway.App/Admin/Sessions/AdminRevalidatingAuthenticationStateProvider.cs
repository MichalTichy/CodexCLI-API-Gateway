using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;

namespace CodexGateway.App.Admin.Sessions;

public sealed class AdminRevalidatingAuthenticationStateProvider(
    ILoggerFactory loggerFactory,
    AdminSessionRegistry sessions)
    : RevalidatingServerAuthenticationStateProvider(loggerFactory)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromSeconds(30);

    protected override Task<bool> ValidateAuthenticationStateAsync(
        AuthenticationState authenticationState,
        CancellationToken cancellationToken) =>
        Task.FromResult(sessions.IsActive(authenticationState.User));

    internal void InvalidateSession()
    {
        SetAuthenticationState(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }
}
