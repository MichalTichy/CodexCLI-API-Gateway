using System.Security.Claims;
using CodexGateway.App.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace CodexGateway.Tests;

public sealed class AdminSessionSecurityTests
{
    [Fact]
    public void Registry_requires_the_registered_session_claim_and_revokes_once()
    {
        var sessions = new AdminSessionRegistry();
        var sessionId = sessions.Register();
        var principal = CreatePrincipal(sessionId);

        Assert.True(sessions.IsActive(sessionId));
        Assert.True(sessions.IsActive(principal));
        Assert.False(sessions.IsActive(CreatePrincipal("another-session")));
        Assert.False(sessions.IsActive(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.True(sessions.Revoke(principal));
        Assert.False(sessions.IsActive(sessionId));
        Assert.False(sessions.Revoke(principal));
    }

    [Fact]
    public async Task Circuit_handler_dispatches_activity_for_an_active_session()
    {
        var sessions = new AdminSessionRegistry();
        var sessionId = sessions.Register();
        var authentication = new FixedAuthenticationStateProvider(CreatePrincipal(sessionId));
        var handler = new AdminSessionCircuitHandler(authentication, sessions);
        var dispatched = false;
        var inboundActivity = handler.CreateInboundActivityHandler(_ =>
        {
            dispatched = true;
            return Task.CompletedTask;
        });

        await inboundActivity(null!);

        Assert.True(dispatched);
    }

    [Fact]
    public async Task Circuit_handler_rechecks_each_activity_and_invalidates_a_revoked_session()
    {
        var sessions = new AdminSessionRegistry();
        var sessionId = sessions.Register();
        using var authentication = new AdminRevalidatingAuthenticationStateProvider(
            NullLoggerFactory.Instance,
            sessions);
        authentication.SetAuthenticationState(Task.FromResult(
            new AuthenticationState(CreatePrincipal(sessionId))));
        var handler = new AdminSessionCircuitHandler(authentication, sessions);
        var dispatchCount = 0;
        var inboundActivity = handler.CreateInboundActivityHandler(_ =>
        {
            dispatchCount++;
            return Task.CompletedTask;
        });

        await inboundActivity(null!);
        Assert.Equal(1, dispatchCount);

        sessions.Revoke(sessionId);
        var exception = await Assert.ThrowsAsync<AdminSessionExpiredException>(
            () => inboundActivity(null!));

        Assert.Equal(1, dispatchCount);
        Assert.Equal("admin_session_expired", exception.Code);
        Assert.Equal(StatusCodes.Status401Unauthorized, exception.StatusCode);
        Assert.False((await authentication.GetAuthenticationStateAsync()).User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task Circuit_handler_allows_anonymous_login_page_activity()
    {
        var sessions = new AdminSessionRegistry();
        var authentication = new FixedAuthenticationStateProvider(
            new ClaimsPrincipal(new ClaimsIdentity()));
        var handler = new AdminSessionCircuitHandler(authentication, sessions);
        var dispatched = false;
        var inboundActivity = handler.CreateInboundActivityHandler(_ =>
        {
            dispatched = true;
            return Task.CompletedTask;
        });

        await inboundActivity(null!);

        Assert.True(dispatched);
    }

    private static ClaimsPrincipal CreatePrincipal(string sessionId) => new(
        new ClaimsIdentity(
            [new Claim(AdminSessionRegistry.SessionIdClaimType, sessionId)],
            CookieAuthenticationDefaults.AuthenticationScheme));

    private sealed class FixedAuthenticationStateProvider(ClaimsPrincipal principal)
        : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
