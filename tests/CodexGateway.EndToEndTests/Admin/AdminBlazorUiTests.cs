using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Admin;

public sealed class AdminBlazorUiTests : IDisposable
{
    private readonly GatewayFactory _factory = new();

    [Fact]
    public async Task Anonymous_admin_page_renders_the_normal_login_form()
    {
        using var client = CreateClient(handleCookies: false);

        using var response = await client.GetAsync("/admin");

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Admin login", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/login\"", html, StringComparison.Ordinal);
        Assert.Contains("method=\"post\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("name=\"username\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"password\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_login_sets_the_admin_cookie_and_renders_the_authorized_dashboard()
    {
        using var client = CreateClient(handleCookies: true);

        using var login = await PostFormAsync(client, "/admin/login", new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "test-password"
        });

        AssertRedirectsTo(login, "/admin");
        Assert.Contains(
            login.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("CodexGateway.Admin=", StringComparison.Ordinal));

        using var dashboard = await client.GetAsync("/admin");
        dashboard.EnsureSuccessStatusCode();
        var html = await dashboard.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
        Assert.Contains("Codex Gateway", html, StringComparison.Ordinal);
        Assert.Contains("action=\"/admin/logout\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Invalid_form_login_redirects_back_with_an_error_and_does_not_authenticate()
    {
        using var client = CreateClient(handleCookies: true);

        using var login = await PostFormAsync(client, "/admin/login", new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "wrong-password"
        });

        AssertRedirectsTo(login, "/admin?error=invalid_credentials");
        Assert.False(login.Headers.TryGetValues("Set-Cookie", out var cookies) &&
                     cookies.Any(value =>
                         value.StartsWith("CodexGateway.Admin=", StringComparison.Ordinal) &&
                         !value.StartsWith("CodexGateway.Admin=;", StringComparison.Ordinal)));

        using var errorPage = await client.GetAsync(login.Headers.Location);
        errorPage.EnsureSuccessStatusCode();
        var html = await errorPage.Content.ReadAsStringAsync();
        Assert.Contains("Admin login", html, StringComparison.Ordinal);
        Assert.Contains("Invalid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_login_and_logout_reject_missing_antiforgery_tokens()
    {
        using var client = CreateClient(handleCookies: true);
        using var login = await client.PostAsync("/admin/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "test-password"
        }));
        Assert.Equal(HttpStatusCode.BadRequest, login.StatusCode);

        await SignInWithFormAsync(client);
        using var logout = await client.PostAsync("/admin/logout", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, logout.StatusCode);

        using var dashboard = await client.GetAsync("/admin");
        dashboard.EnsureSuccessStatusCode();
        Assert.Contains(
            "data-testid=\"admin-dashboard\"",
            await dashboard.Content.ReadAsStringAsync(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_logout_invalidates_the_admin_cookie()
    {
        using var client = CreateClient(handleCookies: true);
        await SignInWithFormAsync(client);

        using var logout = await PostFormAsync(client, "/admin/logout", []);

        AssertRedirectsTo(logout, "/admin?logout=success");
        Assert.Contains(
            logout.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("CodexGateway.Admin=", StringComparison.Ordinal) &&
                     (value.Contains("expires=", StringComparison.OrdinalIgnoreCase) ||
                      value.Contains("max-age=0", StringComparison.OrdinalIgnoreCase)));

        using var page = await client.GetAsync("/admin");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Admin login", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removed_management_json_route_is_not_mapped_for_an_authenticated_admin()
    {
        using var client = CreateClient(handleCookies: true);
        await SignInWithFormAsync(client);

        using var response = await client.GetAsync("/admin/api/projects");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_copied_cookies_that_another_client_still_holds()
    {
        using var browserClient = CreateClient(handleCookies: true);
        using var login = await PostFormAsync(browserClient, "/admin/login", new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "test-password"
        });
        AssertRedirectsTo(login, "/admin");
        var copiedCookie = login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("CodexGateway.Admin=", StringComparison.Ordinal))
            .Split(';', 2)[0];

        using var copiedCookieClient = CreateClient(handleCookies: false);
        copiedCookieClient.DefaultRequestHeaders.Add("Cookie", copiedCookie);
        using (var beforeLogout = await copiedCookieClient.GetAsync("/admin"))
        {
            beforeLogout.EnsureSuccessStatusCode();
            Assert.Contains(
                "data-testid=\"admin-dashboard\"",
                await beforeLogout.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using (var logout = await PostFormAsync(browserClient, "/admin/logout", []))
        {
            AssertRedirectsTo(logout, "/admin?logout=success");
        }

        using var afterLogout = await copiedCookieClient.GetAsync("/admin");
        afterLogout.EnsureSuccessStatusCode();
        var html = await afterLogout.Content.ReadAsStringAsync();
        Assert.Contains("Admin login", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Interactive_server_framework_asset_and_negotiate_endpoint_are_available()
    {
        using var client = CreateClient(handleCookies: false);

        using (var page = await client.GetAsync("/admin"))
        {
            page.EnsureSuccessStatusCode();
            Assert.Contains(
                "_framework/blazor.web.js",
                await page.Content.ReadAsStringAsync(),
                StringComparison.Ordinal);
        }

        using (var script = await client.GetAsync("/_framework/blazor.web.js"))
        {
            var content = await script.Content.ReadAsByteArrayAsync();
            Assert.True(
                script.IsSuccessStatusCode,
                $"Framework asset returned {(int)script.StatusCode}: {Encoding.UTF8.GetString(content)}");
            Assert.NotEmpty(content);
        }

        using var negotiate = await client.PostAsync("/_blazor/negotiate?negotiateVersion=1", null);
        negotiate.EnsureSuccessStatusCode();
        using var payload = JsonDocument.Parse(await negotiate.Content.ReadAsStringAsync());
        Assert.True(payload.RootElement.TryGetProperty("connectionToken", out var connectionToken));
        Assert.False(string.IsNullOrWhiteSpace(connectionToken.GetString()));
        Assert.NotEmpty(payload.RootElement.GetProperty("availableTransports").EnumerateArray());
    }

    [Fact]
    public async Task Admin_cookie_and_openai_bearer_key_remain_isolated()
    {
        using var cookieClient = CreateClient(handleCookies: true);
        await SignInWithFormAsync(cookieClient);

        using (var apiWithoutBearer = await cookieClient.GetAsync("/v1/models"))
        {
            Assert.Equal(HttpStatusCode.Unauthorized, apiWithoutBearer.StatusCode);
        }

        using var bearerClient = CreateClient(handleCookies: false);
        bearerClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", "e2e-api-key");

        using (var models = await bearerClient.GetAsync("/v1/models"))
        {
            models.EnsureSuccessStatusCode();
        }

        using var adminPageWithBearerOnly = await bearerClient.GetAsync("/admin");
        adminPageWithBearerOnly.EnsureSuccessStatusCode();
        var html = await adminPageWithBearerOnly.Content.ReadAsStringAsync();
        Assert.Contains("Admin login", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-testid=\"admin-dashboard\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabling_the_admin_ui_unmaps_the_entire_management_surface_even_with_a_valid_cookie()
    {
        using var enabledClient = CreateClient(handleCookies: true);
        using var login = await PostFormAsync(enabledClient, "/admin/login", new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "test-password"
        });
        AssertRedirectsTo(login, "/admin");
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Single(value => value.StartsWith("CodexGateway.Admin=", StringComparison.Ordinal))
            .Split(';', 2)[0];

        var disabledFactory = new GatewayFactory(adminEnabled: false);
        try
        {
            using var disabledClient = disabledFactory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false
            });
            disabledClient.DefaultRequestHeaders.Add("Cookie", cookie);

            Assert.Equal(HttpStatusCode.NotFound, (await disabledClient.GetAsync("/admin")).StatusCode);
            AssertUnavailable(await disabledClient.PostAsync("/admin/login", null));
            AssertUnavailable(await disabledClient.PostAsync("/admin/logout", null));
        }
        finally
        {
            CleanupFactory(disabledFactory);
        }
    }

    public void Dispose()
    {
        CleanupFactory(_factory);
    }

    private static void CleanupFactory(GatewayFactory factory)
    {
        factory.Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(factory.RootPath); attempt++)
        {
            try
            {
                Directory.Delete(factory.RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    private HttpClient CreateClient(bool handleCookies) => _factory.CreateClient(
        new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = handleCookies
        });

    private static async Task SignInWithFormAsync(HttpClient client)
    {
        using var response = await PostFormAsync(client, "/admin/login", new Dictionary<string, string>
        {
            ["username"] = "test-admin",
            ["password"] = "test-password"
        });
        AssertRedirectsTo(response, "/admin");
    }

    private static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client,
        string path,
        IEnumerable<KeyValuePair<string, string>> values)
    {
        using var page = await client.GetAsync("/admin");
        page.EnsureSuccessStatusCode();
        var html = await page.Content.ReadAsStringAsync();
        var match = Regex.Match(
            html,
            "name=\\\"__RequestVerificationToken\\\"[^>]*value=\\\"(?<token>[^\\\"]+)\\\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The admin page did not render an antiforgery token.");

        var formValues = values.Append(new KeyValuePair<string, string>(
            "__RequestVerificationToken",
            match.Groups["token"].Value));
        return await client.PostAsync(path, new FormUrlEncodedContent(formValues));
    }

    private static void AssertRedirectsTo(HttpResponseMessage response, string expectedLocation)
    {
        Assert.True(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.SeeOther,
            $"Expected a form redirect but received {(int)response.StatusCode} ({response.StatusCode}).");
        Assert.NotNull(response.Headers.Location);
        Assert.Equal(expectedLocation, response.Headers.Location.OriginalString);
    }

    private static void AssertUnavailable(HttpResponseMessage response)
    {
        using (response)
        {
            Assert.True(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"Expected the disabled admin route to be unavailable, but received {(int)response.StatusCode} ({response.StatusCode}).");
        }
    }
}
