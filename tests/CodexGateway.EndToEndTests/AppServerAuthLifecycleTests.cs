using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests;

public sealed class AppServerAuthLifecycleTests
{
    [Fact]
    public async Task Initialize_failure_discards_the_process_and_the_next_request_recovers()
    {
        await using var harness = new LifecycleHarness(appServerRequestTimeoutSeconds: 2);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await harness.EnableScenarioAsync("fail-app-server-initialize-once", deadline.Token);

        using var failed = await harness.Api.GetAsync("/v1/models", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);

        using var recovered = await harness.Api.GetAsync("/v1/models", deadline.Token);
        recovered.EnsureSuccessStatusCode();

        var processIds = await harness.ReadAppServerProcessIdsAsync(deadline.Token);
        Assert.True(processIds.Count >= 2);
        Assert.True(processIds.Distinct().Count() >= 2);
    }

    [Fact]
    public async Task Initialize_timeout_does_not_expose_or_reuse_the_half_initialized_process()
    {
        await using var harness = new LifecycleHarness(appServerRequestTimeoutSeconds: 2);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await harness.EnableScenarioAsync("hang-app-server-initialize-once", deadline.Token);

        var first = harness.Api.GetAsync("/v1/models", deadline.Token);
        var halfInitializedProcess = await harness.WaitForGateAsync("app-server-initialize", deadline.Token);
        var second = harness.Api.GetAsync("/v1/models", deadline.Token);

        using (var failed = await first)
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        }

        using (var recovered = await second)
        {
            recovered.EnsureSuccessStatusCode();
        }

        await WaitForProcessExitAsync(halfInitializedProcess, deadline.Token);
        var processIds = await harness.ReadAppServerProcessIdsAsync(deadline.Token);
        Assert.Contains(halfInitializedProcess, processIds);
        Assert.Contains(processIds, processId => processId != halfInitializedProcess);
    }

    [Fact]
    public async Task Cancelling_the_starting_caller_keeps_run_admission_closed_until_the_login_is_tracked()
    {
        await using var harness = new LifecycleHarness(appServerRequestTimeoutSeconds: 5);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await harness.EnableScenarioAsync("hold-device-login", deadline.Token);
        await harness.EnableScenarioAsync("hold-device-login-start-response", deadline.Token);

        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var starting = harness.Codex.StartDeviceLoginAsync(requestCancellation.Token);
        await harness.WaitForGateAsync("device-login-start", deadline.Token);
        requestCancellation.Cancel();

        var coordinator = harness.Factory.Services.GetRequiredService<RunCoordinator>();
        await Assert.ThrowsAsync<CodexUnavailableException>(() => coordinator.ExecuteAsync(
            projectId: null,
            _ => Task.FromResult(true),
            deadline.Token));

        await harness.ReleaseGateAsync("device-login-start", deadline.Token);
        var started = await starting;
        Assert.Equal(DeviceLoginStatus.Pending, started.Status);
        var login = await harness.WaitForLoginStatusAsync(DeviceLoginStatus.Pending, deadline.Token);
        Assert.Equal("fake-login", login.LoginId);

        using var blocked = await harness.SendChatAsync("while the abandoned request finishes", deadline.Token);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);

        await harness.Codex.CancelDeviceLoginAsync(deadline.Token);
    }

    [Fact]
    public async Task Device_login_timeout_confirms_cancel_before_run_admission_reopens()
    {
        await using var harness = new LifecycleHarness(
            appServerRequestTimeoutSeconds: 5,
            deviceLoginTimeoutSeconds: 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await harness.EnableScenarioAsync("hold-device-login", deadline.Token);
        await harness.EnableScenarioAsync("hold-device-login-cancel-response", deadline.Token);

        await harness.Codex.StartDeviceLoginAsync(deadline.Token);

        await harness.WaitForGateAsync("device-login-cancel", deadline.Token);
        using (var blocked = await harness.SendChatAsync("while timeout cancellation is unacknowledged", deadline.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, blocked.StatusCode);
        }

        var coordinator = harness.Factory.Services.GetRequiredService<RunCoordinator>();
        await Assert.ThrowsAsync<CodexUnavailableException>(() => coordinator.ExecuteAsync(
            projectId: null,
            _ => Task.FromResult(true),
            deadline.Token));

        await harness.ReleaseGateAsync("device-login-cancel", deadline.Token);
        await harness.WaitForLoginStatusAsync(DeviceLoginStatus.Failed, deadline.Token);

        using var recovered = await harness.SendChatAsync("after device-login timeout", deadline.Token);
        recovered.EnsureSuccessStatusCode();
    }

    private static async Task WaitForProcessExitAsync(int processId, CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                if (process.HasExited)
                {
                    return;
                }
            }
            catch (ArgumentException)
            {
                return;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private sealed class LifecycleHarness : IAsyncDisposable
    {
        public LifecycleHarness(int appServerRequestTimeoutSeconds, int? deviceLoginTimeoutSeconds = null)
        {
            Factory = new GatewayFactory(
                appServerRequestTimeoutSeconds: appServerRequestTimeoutSeconds,
                deviceLoginTimeoutSeconds: deviceLoginTimeoutSeconds);
            Api = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Api.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
            Codex = Factory.GetCodexControlPlane();
        }

        public GatewayFactory Factory { get; }

        public HttpClient Api { get; }

        public ICodexControlPlane Codex { get; }

        public Task EnableScenarioAsync(string name, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(Path.Combine(Factory.ScenarioPath, name), string.Empty, cancellationToken);

        public Task<HttpResponseMessage> SendChatAsync(string prompt, CancellationToken cancellationToken) =>
            Api.PostAsJsonAsync("/v1/chat/completions", new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = prompt } }
            }, cancellationToken);

        public async Task<DeviceLogin> WaitForLoginStatusAsync(DeviceLoginStatus expected, CancellationToken cancellationToken)
        {
            while (true)
            {
                var login = await Codex.GetDeviceLoginAsync(cancellationToken);
                if (login?.Status == expected)
                {
                    return login;
                }

                await Task.Delay(10, cancellationToken);
            }
        }

        public async Task<int> WaitForGateAsync(string name, CancellationToken cancellationToken)
        {
            var path = GatePath(name, "started.json");
            while (true)
            {
                if (!File.Exists(path))
                {
                    await Task.Delay(10, cancellationToken);
                    continue;
                }

                try
                {
                    await using var stream = File.OpenRead(path);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    return document.RootElement.GetProperty("processId").GetInt32();
                }
                catch (IOException)
                {
                    // The fake process publishes the gate atomically enough for production behavior,
                    // but Windows can expose the path briefly before releasing the writer handle.
                }
                catch (JsonException)
                {
                    // Retry if the reader observed the file before the JSON payload was complete.
                }

                await Task.Delay(10, cancellationToken);
            }
        }

        public Task ReleaseGateAsync(string name, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(GatePath(name, "release"), string.Empty, cancellationToken);

        public async Task<IReadOnlyList<int>> ReadAppServerProcessIdsAsync(CancellationToken cancellationToken)
        {
            var directory = Path.Combine(Factory.ScenarioPath, "invocations");
            var processIds = new List<int>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                await using var stream = File.OpenRead(path);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.GetProperty("mode").GetString() == "app-server")
                {
                    processIds.Add(document.RootElement.GetProperty("processId").GetInt32());
                }
            }

            return processIds;
        }

        public ValueTask DisposeAsync()
        {
            Api.Dispose();
            Factory.Services.GetRequiredService<CodexAppServerClient>().Dispose();
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

        private string GatePath(string name, string suffix) =>
            Path.Combine(Factory.ScenarioPath, "gates", name + "." + suffix);
    }
}
