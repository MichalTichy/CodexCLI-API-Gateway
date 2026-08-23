using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;

namespace CodexGateway.App.Admin.Authentication;

public static class AdminAuthenticationRoutes
{
    public static void MapAdminAuthenticationRoutes(this WebApplication app)
    {
        app.MapPost(
            "/admin/login",
            (Func<HttpContext, IOptions<AdminUiOptions>, IAntiforgery, AdminSessionRegistry, Task<IResult>>)LoginAsync)
            .AllowAnonymous();
        app.MapPost(
            "/admin/logout",
            (Func<HttpContext, IAntiforgery, AdminSessionRegistry, Task<IResult>>)LogoutAsync)
            .RequireAuthorization();
    }

    private static async Task<IResult> LoginAsync(
        HttpContext context,
        IOptions<AdminUiOptions> options,
        IAntiforgery antiforgery,
        AdminSessionRegistry sessions)
    {
        if (!context.Request.HasFormContentType || !await IsAntiforgeryValidAsync(context, antiforgery))
        {
            return Results.BadRequest();
        }

        var form = await context.Request.ReadFormAsync(context.RequestAborted);
        var configured = options.Value;
        var usernameMatches = FixedEquals(form["username"].ToString(), configured.Username);
        var passwordMatches = FixedEquals(form["password"].ToString(), configured.Password);
        if (!configured.Enabled || !usernameMatches || !passwordMatches)
        {
            return Results.Redirect("/admin?error=invalid_credentials");
        }

        var sessionId = sessions.Register();
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, configured.Username),
                new Claim(AdminSessionRegistry.SessionIdClaimType, sessionId)
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        try
        {
            await context.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity),
                new AuthenticationProperties { IsPersistent = false });
        }
        catch
        {
            sessions.Revoke(sessionId);
            throw;
        }

        return Results.Redirect("/admin");
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        AdminSessionRegistry sessions)
    {
        if (!context.Request.HasFormContentType || !await IsAntiforgeryValidAsync(context, antiforgery))
        {
            return Results.BadRequest();
        }

        sessions.Revoke(context.User);
        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/admin?logout=success");
    }

    private static async Task<bool> IsAntiforgeryValidAsync(HttpContext context, IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException)
        {
            return false;
        }
    }

    private static bool FixedEquals(string supplied, string configured)
    {
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied ?? string.Empty));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured ?? string.Empty));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }
}
