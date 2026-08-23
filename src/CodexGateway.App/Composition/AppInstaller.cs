using System.Text.Json;
using System.Text.Json.Serialization;
using CodexGateway.IoC;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.App.Composition;

public sealed class AppInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<AdminUiOptions>()
            .Bind(configuration.GetSection(AdminUiOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => !options.Enabled || environment.IsDevelopment() ||
                           options.Password != "internal-password",
                "AdminUi:Password must be changed outside Development.")
            .ValidateOnStart();

        services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "CodexGateway.Admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.SlidingExpiration = false;
                options.LoginPath = "/admin";
                options.AccessDeniedPath = "/admin";
                options.Events.OnRedirectToLogin = context =>
                {
                    if (IsInteractiveAdminRequest(context.Request))
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    if (IsInteractiveAdminRequest(context.Request))
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }
                    else
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    }

                    return Task.CompletedTask;
                };
                options.Events.OnValidatePrincipal = async context =>
                {
                    var sessions = context.HttpContext.RequestServices
                        .GetRequiredService<AdminSessionRegistry>();
                    if (!sessions.IsActive(context.Principal))
                    {
                        context.RejectPrincipal();
                        await context.HttpContext.SignOutAsync(
                            CookieAuthenticationDefaults.AuthenticationScheme);
                    }
                };
            });

        services.AddAuthorization();
        services.AddCascadingAuthenticationState();
        services.AddRazorComponents()
            .AddInteractiveServerComponents();
        services.AddSingleton<AdminSessionRegistry>();
        services.AddScoped<AdminRevalidatingAuthenticationStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(provider =>
            provider.GetRequiredService<AdminRevalidatingAuthenticationStateProvider>());
        services.AddScoped<CircuitHandler, AdminSessionCircuitHandler>();
    }

    private static bool IsInteractiveAdminRequest(HttpRequest request) =>
        request.Path.StartsWithSegments("/admin");
}
