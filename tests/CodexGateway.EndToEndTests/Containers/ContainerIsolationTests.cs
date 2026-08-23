using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Containers;

public sealed class ContainerIsolationTests
{
    [Fact]
    public async Task Every_chat_crosses_the_hardened_container_boundary_without_a_process_runner_escape()
    {
        await using var harness = new ContainerHarness();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        using (var first = await harness.SendChatAsync("first container run", deadline.Token))
        {
            first.EnsureSuccessStatusCode();
        }

        using (var second = await harness.SendChatAsync("second container run", deadline.Token))
        {
            second.EnsureSuccessStatusCode();
        }

        var runs = await harness.WaitForInvocationsAsync(
            invocation => invocation.Mode == "container-engine" && invocation.Arguments.FirstOrDefault() == "run",
            expectedCount: 2,
            deadline.Token);
        var execs = await harness.WaitForInvocationsAsync(
            invocation => invocation.Mode == "exec",
            expectedCount: 2,
            deadline.Token);

        Assert.Equal(2, runs.Count);
        Assert.Equal(2, execs.Count);
        foreach (var run in runs)
        {
            Assert.DoesNotContain("--rm", run.Arguments);
            Assert.Contains("--init", run.Arguments);
            Assert.Contains("--read-only", run.Arguments);
            AssertOption(run.Arguments, "--cap-drop", "ALL");
            AssertOption(run.Arguments, "--security-opt", "no-new-privileges");
            AssertOption(run.Arguments, "--security-opt", "seccomp=unconfined");
            AssertOption(run.Arguments, "--network", "codex-gateway-e2e");
            AssertOption(run.Arguments, "--pids-limit", "64");
            AssertOption(run.Arguments, "--entrypoint", "codex");
            Assert.True(HasOption(run.Arguments, "--memory"), "The container must have a memory limit.");
            Assert.True(HasOption(run.Arguments, "--cpus"), "The container must have a CPU limit.");
            Assert.Contains(
                ReadOptionValues(run.Arguments, "--tmpfs"),
                value => value.StartsWith("/tmp:", StringComparison.Ordinal) &&
                         value.Contains("size=64m", StringComparison.Ordinal));
            var labels = ReadOptionValues(run.Arguments, "--label");
            Assert.Contains(
                labels,
                value => value == "com.codex-gateway.managed=true");
            Assert.Contains(
                labels,
                value => value.StartsWith("com.codex-gateway.run-id=", StringComparison.Ordinal));
            var instanceLabel = Assert.Single(
                labels,
                value => value.StartsWith("com.codex-gateway.instance-id=", StringComparison.Ordinal));
            var instanceId = instanceLabel["com.codex-gateway.instance-id=".Length..];
            Assert.Equal(32, instanceId.Length);
            Assert.All(instanceId, character => Assert.True(
                character is >= '0' and <= '9' or >= 'a' and <= 'f'));

            var mounts = ReadOptionValues(run.Arguments, "--mount");
            Assert.Contains(mounts, value => IsBindMountTo(value, "/workspace"));
            Assert.Contains(mounts, value =>
                IsBindMountTo(value, "/codex-home") &&
                value.Contains(harness.Factory.CodexHomePath, StringComparison.OrdinalIgnoreCase));
            AssertOption(run.Arguments, "--workdir", "/workspace");

            var imageIndex = Array.IndexOf(run.Arguments, "codex-gateway-runner:e2e");
            Assert.True(imageIndex >= 0, "The configured, pinned runner image must be used.");
            var innerArguments = run.Arguments[(imageIndex + 1)..];
            Assert.Equal("exec", innerArguments[0]);
            var joinedInnerArguments = string.Join('\0', innerArguments);
            Assert.DoesNotContain("first container run", joinedInnerArguments, StringComparison.Ordinal);
            Assert.DoesNotContain("second container run", joinedInnerArguments, StringComparison.Ordinal);

            var matchingExec = Assert.Single(execs, invocation => invocation.ProcessId == run.ProcessId);
            Assert.Equal(innerArguments, matchingExec.Arguments);
        }

        // A direct-process fallback would create an exec record whose process was not
        // also the process emulating `container run`.
        Assert.All(execs, exec => Assert.Contains(runs, run => run.ProcessId == exec.ProcessId));
        foreach (var run in runs)
        {
            await harness.WaitForRemovalAsync(ReadRequiredOption(run.Arguments, "--name"), deadline.Token);
        }
    }

    [Fact]
    public async Task Run_containers_are_force_removed_after_success_client_cancellation_and_timeout()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        await using (var success = new ContainerHarness())
        {
            var responseTask = success.SendChatAsync("successful cleanup", deadline.Token);
            var run = await success.WaitForRunAsync(deadline.Token);
            using var response = await responseTask;
            response.EnsureSuccessStatusCode();
            await success.WaitForRemovalAsync(ReadRequiredOption(run.Arguments, "--name"), deadline.Token);
        }

        await using (var cancelled = new ContainerHarness())
        {
            using var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            var responseTask = cancelled.SendChatAsync("[scenario:hang]", requestCancellation.Token);
            var run = await cancelled.WaitForRunAsync(deadline.Token);
            requestCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            {
                using var _ = await responseTask;
            });
            await cancelled.WaitForRemovalAsync(ReadRequiredOption(run.Arguments, "--name"), deadline.Token);
        }

        await using (var timedOut = new ContainerHarness(timeoutSeconds: 1))
        {
            var responseTask = timedOut.SendChatAsync("[scenario:hang]", deadline.Token);
            var run = await timedOut.WaitForRunAsync(deadline.Token);
            using var response = await responseTask;
            Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);
            await timedOut.WaitForRemovalAsync(ReadRequiredOption(run.Arguments, "--name"), deadline.Token);
        }
    }

    private static bool IsBindMountTo(string specification, string target) =>
        specification.Contains("type=bind", StringComparison.Ordinal) &&
        (specification.Contains($"dst={target}", StringComparison.Ordinal) ||
         specification.Contains($"target={target}", StringComparison.Ordinal) ||
         specification.Contains($"destination={target}", StringComparison.Ordinal));

    private static void AssertOption(IReadOnlyList<string> arguments, string name, string expected) =>
        Assert.Contains(expected, ReadOptionValues(arguments, name));

    private static bool HasOption(IReadOnlyList<string> arguments, string name) =>
        arguments.Any(value => value == name || value.StartsWith(name + "=", StringComparison.Ordinal));

    private static string ReadRequiredOption(IReadOnlyList<string> arguments, string name) =>
        Assert.Single(ReadOptionValues(arguments, name));

    private static string[] ReadOptionValues(IReadOnlyList<string> arguments, string name)
    {
        var result = new List<string>();
        for (var index = 0; index < arguments.Count; index++)
        {
            if (arguments[index] == name && index + 1 < arguments.Count)
            {
                result.Add(arguments[++index]);
            }
            else if (arguments[index].StartsWith(name + "=", StringComparison.Ordinal))
            {
                result.Add(arguments[index][(name.Length + 1)..]);
            }
        }

        return [.. result];
    }

    private sealed class ContainerHarness : IAsyncDisposable
    {
        public ContainerHarness(int? timeoutSeconds = null)
        {
            Factory = new GatewayFactory(timeoutSeconds: timeoutSeconds);
            Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
            Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
        }

        public GatewayFactory Factory { get; }

        private HttpClient Client { get; }

        public Task<HttpResponseMessage> SendChatAsync(string prompt, CancellationToken cancellationToken) =>
            Client.PostAsJsonAsync("/v1/chat/completions", new
            {
                model = "gpt-test-sol",
                messages = new[] { new { role = "user", content = prompt } }
            }, cancellationToken);

        public async Task<Invocation> WaitForRunAsync(CancellationToken cancellationToken) =>
            Assert.Single(await WaitForInvocationsAsync(
                invocation => invocation.Mode == "container-engine" && invocation.Arguments.FirstOrDefault() == "run",
                expectedCount: 1,
                cancellationToken));

        public async Task<IReadOnlyList<Invocation>> WaitForInvocationsAsync(
            Func<Invocation, bool> predicate,
            int expectedCount,
            CancellationToken cancellationToken)
        {
            while (true)
            {
                var matches = ReadInvocations().Where(predicate).ToArray();
                if (matches.Length >= expectedCount)
                {
                    return matches;
                }

                await Task.Delay(20, cancellationToken);
            }
        }

        public async Task WaitForRemovalAsync(string containerName, CancellationToken cancellationToken)
        {
            _ = await WaitForInvocationsAsync(
                invocation => invocation.Mode == "container-engine" &&
                              invocation.Arguments.FirstOrDefault() == "rm" &&
                              (invocation.Arguments.Contains("-f", StringComparer.Ordinal) ||
                               invocation.Arguments.Contains("--force", StringComparer.Ordinal)) &&
                              invocation.Arguments.Contains(containerName, StringComparer.Ordinal),
                expectedCount: 1,
                cancellationToken);
        }

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

        private IEnumerable<Invocation> ReadInvocations()
        {
            var directory = Path.Combine(Factory.ScenarioPath, "invocations");
            if (!Directory.Exists(directory))
            {
                return [];
            }

            var result = new List<Invocation>();
            foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            {
                try
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(path));
                    var root = document.RootElement;
                    result.Add(new Invocation(
                        root.GetProperty("mode").GetString()!,
                        root.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray(),
                        root.GetProperty("processId").GetInt32()));
                }
                catch (IOException)
                {
                }
                catch (JsonException)
                {
                }
            }

            return result;
        }
    }

    private sealed record Invocation(string Mode, string[] Arguments, int ProcessId);
}
