using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexGateway.Logic.Codex;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CodexGateway.EndToEndTests.Authentication;

public sealed class HostCliAuthLifecycleTests
{
    [Fact]
    public async Task Model_catalog_comes_from_configuration_without_starting_a_host_app_server()
    {
        await using var harness = new AuthenticationHarness();

        using var response = await harness.Api.GetAsync("/v1/models");

        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(
            ["gpt-test-sol", "gpt-test-terra"],
            document.RootElement.GetProperty("data").EnumerateArray()
                .Select(model => model.GetProperty("id").GetString()!)
                .ToArray());
        Assert.DoesNotContain(
            await harness.ReadInvocationModesAsync(),
            mode => mode == "app-server");
    }

    [Fact]
    public async Task Device_login_exposes_only_the_verification_url_and_one_time_code()
    {
        await using var harness = new AuthenticationHarness();
        await harness.SignOutFakeAsync();
        await harness.HoldLoginAsync();

        var login = await harness.Authentication.StartDeviceLoginAsync(CancellationToken.None);

        Assert.Equal(DeviceLoginStatus.Pending, login.Status);
        Assert.Equal("https://example.test/device", login.VerificationUrl);
        Assert.Equal("TEST-CODE5", login.UserCode);
        var invocation = Assert.Single(await harness.ReadInvocationsAsync("device-login"));
        Assert.Equal(Path.GetFullPath(harness.Factory.CodexHomePath), invocation.CodexHome);

        await harness.Authentication.CancelDeviceLoginAsync(CancellationToken.None);
        Assert.Equal(
            DeviceLoginStatus.Cancelled,
            (await harness.Authentication.GetDeviceLoginAsync(CancellationToken.None))?.Status);
    }

    [Fact]
    public async Task Completing_device_login_updates_the_shared_auth_state()
    {
        await using var harness = new AuthenticationHarness();
        await harness.SignOutFakeAsync();

        await harness.Authentication.StartDeviceLoginAsync(CancellationToken.None);
        await harness.WaitForLoginStatusAsync(DeviceLoginStatus.Completed);

        Assert.True((await harness.Authentication.GetAccountAsync(CancellationToken.None)).Authenticated);
    }

    [Fact]
    public async Task Logout_uses_the_same_codex_home_and_clears_authentication()
    {
        await using var harness = new AuthenticationHarness();

        Assert.True((await harness.Authentication.GetAccountAsync(CancellationToken.None)).Authenticated);
        await harness.Authentication.LogoutAsync(CancellationToken.None);

        Assert.False((await harness.Authentication.GetAccountAsync(CancellationToken.None)).Authenticated);
        var invocation = Assert.Single(await harness.ReadInvocationsAsync("logout"));
        Assert.Equal(Path.GetFullPath(harness.Factory.CodexHomePath), invocation.CodexHome);
    }

    private sealed class AuthenticationHarness : IAsyncDisposable
    {
        public AuthenticationHarness()
        {
            Factory = new GatewayFactory();
            Api = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
            Authentication = Factory.GetCodexAuthenticationManager();
        }

        public GatewayFactory Factory { get; }

        public HttpClient Api { get; }

        public ICodexAuthenticationManager Authentication { get; }

        public Task SignOutFakeAsync()
        {
            File.Delete(Path.Combine(Factory.ScenarioPath, "authenticated"));
            return Task.CompletedTask;
        }

        public Task HoldLoginAsync() =>
            File.WriteAllTextAsync(Path.Combine(Factory.ScenarioPath, "hold-device-login"), string.Empty);

        public async Task WaitForLoginStatusAsync(DeviceLoginStatus expected)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (true)
            {
                if ((await Authentication.GetDeviceLoginAsync(timeout.Token))?.Status == expected)
                {
                    return;
                }

                await Task.Delay(10, timeout.Token);
            }
        }

        public async Task<IReadOnlyList<string?>> ReadInvocationModesAsync() =>
            (await ReadInvocationsAsync()).Select(invocation => invocation.Mode).ToArray();

        public async Task<IReadOnlyList<Invocation>> ReadInvocationsAsync(string? mode = null)
        {
            var directory = Path.Combine(Factory.ScenarioPath, "invocations");
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var result = new List<Invocation>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream);
                var invocationMode = document.RootElement.GetProperty("mode").GetString();
                if (mode is null || invocationMode == mode)
                {
                    result.Add(new Invocation(
                        invocationMode,
                        document.RootElement.GetProperty("codexHome").GetString()));
                }
            }

            return result;
        }

        public ValueTask DisposeAsync()
        {
            Api.Dispose();
            Factory.Dispose();
            for (var attempt = 0; attempt < 20 && Directory.Exists(Factory.RootPath); attempt++)
            {
                try
                {
                    Directory.Delete(Factory.RootPath, true);
                }
                catch (IOException) when (attempt < 19)
                {
                    Thread.Sleep(50);
                }
            }

            return ValueTask.CompletedTask;
        }
    }

    private sealed record Invocation(string? Mode, string? CodexHome);
}
