using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace CodexGateway.EndToEndTests.Runs;

public sealed class QueueBehaviorTests : IDisposable
{
    private readonly GatewayFactory _factory = new(maxConcurrent: 1, maxQueued: 0);
    private readonly HttpClient _client;

    public QueueBehaviorTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
    }

    [Fact]
    public async Task Full_queue_rejects_streaming_before_sse_headers_are_started()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var running = _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            messages = new[] { new { role = "user", content = "[scenario:hang]" } }
        }, cancellation.Token);

        await WaitForExecInvocationAsync(cancellation.Token);
        var overflow = await _client.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            stream = true,
            messages = new[] { new { role = "user", content = "second" } }
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, overflow.StatusCode);
        Assert.Equal("application/json", overflow.Content.Headers.ContentType?.MediaType);
        var error = await overflow.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_limit_exceeded", error.GetProperty("error").GetProperty("code").GetString());

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await running);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(_factory.RootPath); attempt++)
        {
            try
            {
                Directory.Delete(_factory.RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    private async Task WaitForExecInvocationAsync(CancellationToken cancellationToken)
    {
        var directory = Path.Combine(_factory.ScenarioPath, "invocations");
        while (!cancellationToken.IsCancellationRequested)
        {
            if (Directory.Exists(directory) && Directory.EnumerateFiles(directory, "*.json").Any(IsCompletedExecRecord))
            {
                return;
            }

            await Task.Delay(25, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool IsCompletedExecRecord(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.GetProperty("mode").GetString() == "exec";
        }
        catch (IOException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
