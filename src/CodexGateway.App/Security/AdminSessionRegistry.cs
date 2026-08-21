using System.Collections.Concurrent;
using System.Security.Claims;

namespace CodexGateway.App.Security;

/// <summary>
/// Tracks the server-side lifetime of authenticated administration sessions.
/// Cookies are only accepted while their corresponding session remains registered.
/// </summary>
public sealed class AdminSessionRegistry
{
    public const string SessionIdClaimType = "codex_gateway_admin_session_id";

    private readonly ConcurrentDictionary<string, byte> _sessions =
        new(StringComparer.Ordinal);

    public string Register()
    {
        while (true)
        {
            var sessionId = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            if (_sessions.TryAdd(sessionId, 0))
            {
                return sessionId;
            }
        }
    }

    public bool IsActive(string? sessionId) =>
        !string.IsNullOrWhiteSpace(sessionId) && _sessions.ContainsKey(sessionId);

    public bool IsActive(ClaimsPrincipal? principal) =>
        principal?.Identity?.IsAuthenticated == true && IsActive(GetSessionId(principal));

    public bool Revoke(string? sessionId)
    {
        return !string.IsNullOrWhiteSpace(sessionId) && _sessions.TryRemove(sessionId, out _);
    }

    public bool Revoke(ClaimsPrincipal? principal) => Revoke(GetSessionId(principal));

    public static string? GetSessionId(ClaimsPrincipal? principal) =>
        principal?.FindFirstValue(SessionIdClaimType);
}
