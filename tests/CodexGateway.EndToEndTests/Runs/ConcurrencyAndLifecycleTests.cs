using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using CodexGateway.Logic.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Runs;

public sealed class ConcurrencyAndLifecycleTests
{
    [Fact]
    public async Task Same_project_runs_are_serialized_before_the_cli_process_starts()
    {
        await using var harness = new GatewayHarness(maxConcurrent: 2, maxQueued: 2);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await harness.CreateProjectAsync("serial", deadline.Token);

        var first = harness.SendChatAsync("/p/serial/v1/chat/completions", "[scenario:gate:serial-first]", deadline.Token);
        await harness.WaitForGateAsync("serial-first", deadline.Token);
        var second = harness.SendChatAsync("/p/serial/v1/chat/completions", "[scenario:gate:serial-second]", deadline.Token);

        var coordinator = harness.Factory.Services.GetRequiredService<RunCoordinator>();
        await WaitUntilAsync(() => coordinator.AdmittedRuns == 2, deadline.Token);
        Assert.Equal(1, coordinator.ActiveRuns);
        Assert.False(harness.HasGateStarted("serial-second"));

        await harness.ReleaseGateAsync("serial-first", deadline.Token);
        using (var firstResponse = await first)
        {
            firstResponse.EnsureSuccessStatusCode();
        }

        await harness.WaitForGateAsync("serial-second", deadline.Token);
        await harness.ReleaseGateAsync("serial-second", deadline.Token);
        using var secondResponse = await second;
        secondResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Different_project_runs_can_execute_in_parallel()
    {
        await using var harness = new GatewayHarness(maxConcurrent: 2, maxQueued: 2);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await harness.CreateProjectAsync("parallel-a", deadline.Token);
        await harness.CreateProjectAsync("parallel-b", deadline.Token);

        var first = harness.SendChatAsync("/p/parallel-a/v1/chat/completions", "[scenario:gate:parallel-a]", deadline.Token);
        var second = harness.SendChatAsync("/p/parallel-b/v1/chat/completions", "[scenario:gate:parallel-b]", deadline.Token);

        await Task.WhenAll(
            harness.WaitForGateAsync("parallel-a", deadline.Token),
            harness.WaitForGateAsync("parallel-b", deadline.Token));
        Assert.Equal(2, harness.Factory.Services.GetRequiredService<RunCoordinator>().ActiveRuns);

        await Task.WhenAll(
            harness.ReleaseGateAsync("parallel-a", deadline.Token),
            harness.ReleaseGateAsync("parallel-b", deadline.Token));
        using var firstResponse = await first;
        using var secondResponse = await second;
        firstResponse.EnsureSuccessStatusCode();
        secondResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Client_cancellation_kills_the_cli_process_and_releases_capacity()
    {
        await using var harness = new GatewayHarness(maxConcurrent: 1, maxQueued: 0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        var running = harness.SendChatAsync(
            "/v1/chat/completions",
            "[scenario:gate:cancel-client]",
            requestCancellation.Token);
        var processId = await harness.WaitForGateAsync("cancel-client", deadline.Token);

        requestCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            using var _ = await running;
        });
        await WaitForProcessExitAsync(processId, deadline.Token);
        var coordinator = harness.Factory.Services.GetRequiredService<RunCoordinator>();
        await WaitUntilAsync(() => coordinator.AdmittedRuns == 0 && coordinator.ActiveRuns == 0, deadline.Token);

        using var recovered = await harness.SendChatAsync("/v1/chat/completions", "after cancellation", deadline.Token);
        recovered.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Run_timeout_returns_504_kills_the_cli_process_and_releases_capacity()
    {
        await using var harness = new GatewayHarness(maxConcurrent: 1, maxQueued: 0, timeoutSeconds: 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = harness.SendChatAsync("/v1/chat/completions", "[scenario:gate:server-timeout]", deadline.Token);
        var processId = await harness.WaitForGateAsync("server-timeout", deadline.Token);

        using var timedOut = await running;
        Assert.Equal(HttpStatusCode.GatewayTimeout, timedOut.StatusCode);
        var error = await timedOut.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
        Assert.Equal("run_timeout", error.GetProperty("error").GetProperty("code").GetString());
        await WaitForProcessExitAsync(processId, deadline.Token);

        using var recovered = await harness.SendChatAsync("/v1/chat/completions", "after timeout", deadline.Token);
        recovered.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Stderr_larger_than_the_pipe_buffer_is_drained_without_deadlock()
    {
        await using var harness = new GatewayHarness(maxConcurrent: 1, maxQueued: 0);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var response = await harness.SendChatAsync(
            "/v1/chat/completions",
            "[scenario:stderr-flood]",
            deadline.Token);

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
        Assert.Equal(
            "Fake Codex response",
            body.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
    }

    [Fact]
    public async Task Oversized_file_outside_artifacts_terminates_the_running_cli_and_returns_the_safe_limit_error()
    {
        await using var harness = new GatewayHarness(
            maxConcurrent: 1,
            maxQueued: 0,
            maxArtifactFileMegabytes: 1);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = harness.SendChatAsync(
            "/v1/chat/completions",
            "[scenario:gate:artifact-quota][scenario:workspace-bytes:2097152]",
            deadline.Token);
        var processId = await harness.WaitForGateAsync("artifact-quota", deadline.Token);

        await harness.ReleaseGateAsync("artifact-quota", deadline.Token);
        using var response = await running;

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(deadline.Token);
        Assert.Equal("artifact_limit_exceeded", body.GetProperty("error").GetProperty("code").GetString());
        Assert.DoesNotContain(harness.Factory.RootPath, body.ToString(), StringComparison.OrdinalIgnoreCase);
        await WaitForProcessExitAsync(processId, deadline.Token);

        using var recovered = await harness.SendChatAsync("/v1/chat/completions", "after artifact quota", deadline.Token);
        recovered.EnsureSuccessStatusCode();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            await Task.Delay(10, cancellationToken);
        }
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

    private sealed class GatewayHarness : IAsyncDisposable
    {
        public GatewayHarness(
            int maxConcurrent,
            int maxQueued,
            int? timeoutSeconds = null,
            int? maxArtifactFileMegabytes = null)
        {
            Factory = new GatewayFactory(
                maxConcurrent,
                maxQueued,
                timeoutSeconds,
                maxArtifactFileMegabytes: maxArtifactFileMegabytes);
            Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
        }

        public GatewayFactory Factory { get; }

        private HttpClient Client { get; }

        public async Task CreateProjectAsync(string id, CancellationToken cancellationToken)
        {
            await Factory.CreateProjectAsync(id, id, cancellationToken: cancellationToken);
        }

        public Task<HttpResponseMessage> SendChatAsync(string path, string prompt, CancellationToken cancellationToken) =>
            Client.PostAsJsonAsync(path, new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = prompt } }
            }, cancellationToken);

        public bool HasGateStarted(string gateName) => File.Exists(GatePath(gateName, "started.json"));

        public async Task<int> WaitForGateAsync(string gateName, CancellationToken cancellationToken)
        {
            var path = GatePath(gateName, "started.json");
            while (true)
            {
                await WaitUntilAsync(() => File.Exists(path), cancellationToken);
                try
                {
                    await using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    return document.RootElement.GetProperty("processId").GetInt32();
                }
                catch (IOException)
                {
                    await Task.Delay(10, cancellationToken);
                }
                catch (JsonException)
                {
                    await Task.Delay(10, cancellationToken);
                }
            }
        }

        public Task ReleaseGateAsync(string gateName, CancellationToken cancellationToken) =>
            File.WriteAllTextAsync(GatePath(gateName, "release"), string.Empty, cancellationToken);

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
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

        private string GatePath(string gateName, string suffix) =>
            Path.Combine(Factory.ScenarioPath, "gates", gateName + "." + suffix);
    }
}
