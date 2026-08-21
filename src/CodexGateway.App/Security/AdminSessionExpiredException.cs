using CodexGateway.Logic.Errors;

namespace CodexGateway.App.Security;

public sealed class AdminSessionExpiredException()
    : GatewayException(
        StatusCodes.Status401Unauthorized,
        "admin_session_expired",
        "Your admin session has ended. Sign in again.");
