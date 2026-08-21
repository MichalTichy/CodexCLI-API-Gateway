using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;

namespace CodexGateway.App.Security;

/// <summary>
/// Rejects browser events for a revoked session before component event handlers run.
/// </summary>
public sealed class AdminSessionCircuitHandler(
    AuthenticationStateProvider authenticationStateProvider,
    AdminSessionRegistry sessions) : CircuitHandler
{
    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(
        Func<CircuitInboundActivityContext, Task> next) => async context =>
        {
            var authenticationState = await authenticationStateProvider.GetAuthenticationStateAsync();
            if (authenticationState.User.Identity?.IsAuthenticated != true)
            {
                await next(context);
                return;
            }

            if (!sessions.IsActive(authenticationState.User))
            {
                if (authenticationStateProvider is AdminRevalidatingAuthenticationStateProvider provider)
                {
                    provider.InvalidateSession();
                }

                throw new AdminSessionExpiredException();
            }

            await next(context);
        };
}
