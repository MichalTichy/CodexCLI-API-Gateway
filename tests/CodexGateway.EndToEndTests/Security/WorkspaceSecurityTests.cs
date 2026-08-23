using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;

namespace CodexGateway.EndToEndTests.Security;

public sealed class WorkspaceSecurityTests : IDisposable
{
    private readonly GatewayFactory _factory = new();
    private readonly HttpClient _client;
    private readonly List<string> _directoryLinks = [];

    public WorkspaceSecurityTests()
    {
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "e2e-api-key");
    }

    [Fact]
    public async Task Project_chat_rejects_nested_artifact_links_with_a_safe_openai_error()
    {
        await _factory.CreateProjectAsync("linked-artifacts", "Linked artifacts");

        var artifacts = Path.Combine(_factory.StoragePath, "projects", "linked-artifacts", "artifacts");
        var outside = Path.Combine(_factory.RootPath, "outside-artifacts");
        Directory.CreateDirectory(outside);
        var outsideFile = Path.Combine(outside, "do-not-copy.txt");
        await File.WriteAllTextAsync(outsideFile, "outside secret");
        CreateDirectoryLink(Path.Combine(artifacts, "escape"), outside);

        var response = await _client.PostAsJsonAsync("/p/linked-artifacts/v1/chat/completions", new
        {
            model = "gpt-test-sol",
            messages = new[] { new { role = "user", content = "read the linked artifact" } }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var error = json.GetProperty("error");
        Assert.Equal("unsafe_artifact_path", error.GetProperty("code").GetString());
        Assert.Equal("invalid_request_error", error.GetProperty("type").GetString());
        var responseBody = json.ToString();
        Assert.DoesNotContain(_factory.RootPath, responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("outside secret", responseBody, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("outside secret", await File.ReadAllTextAsync(outsideFile));

        var invocationDirectory = Path.Combine(_factory.ScenarioPath, "invocations");
        var execWasInvoked = Directory.EnumerateFiles(invocationDirectory, "*.json")
            .Select(File.ReadAllText)
            .Any(content => content.Contains("\"mode\": \"exec\"", StringComparison.Ordinal));
        Assert.False(execWasInvoked);
    }

    public void Dispose()
    {
        foreach (var link in _directoryLinks.AsEnumerable().Reverse())
        {
            RemoveDirectoryLink(link);
        }

        _client.Dispose();
        _factory.Services.GetRequiredService<CodexAppServerClient>().Dispose();
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

    private void CreateDirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            _directoryLinks.Add(link);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("mklink");
        startInfo.ArgumentList.Add("/J");
        startInfo.ArgumentList.Add(link);
        startInfo.ArgumentList.Add(target);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not create a test junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not create a test junction: " + process.StandardError.ReadToEnd());
        }

        _directoryLinks.Add(link);
    }

    private static void RemoveDirectoryLink(string link)
    {
        if (!Directory.Exists(link) && !File.Exists(link))
        {
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            Directory.Delete(link);
            return;
        }

        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("rmdir");
        startInfo.ArgumentList.Add(link);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not remove a test junction.");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Could not remove a test junction: " + process.StandardError.ReadToEnd());
        }
    }
}
