using CodexGateway.Infrastructure.Codex;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using CodexGateway.Models;

namespace CodexGateway.OpenAICompatibilityTests;

public sealed class CompatibilityGatewayFactory : WebApplicationFactory<Program>
{
    public CompatibilityGatewayFactory()
    {
        RootPath = Path.Combine(Path.GetTempPath(), "codex-gateway-openai-compat", Guid.NewGuid().ToString("N"));
        StoragePath = Path.Combine(RootPath, "data");
        ScenarioPath = Path.Combine(RootPath, "fake");
        CodexHomePath = Path.Combine(RootPath, "codex-home");
        Directory.CreateDirectory(StoragePath);
        Directory.CreateDirectory(ScenarioPath);
        Directory.CreateDirectory(CodexHomePath);
        File.WriteAllText(
            Path.Combine(StoragePath, "state.json"),
            JsonSerializer.Serialize(new GatewayState
            {
                ApiKeys =
                [
                    new GatewayApiKeyDefinition { Id = "default", Name = "Default test key", Key = "e2e-api-key" }
                ]
            }));
    }

    public string RootPath { get; }

    public string StoragePath { get; }

    public string ScenarioPath { get; }

    public string CodexHomePath { get; }

    public void StopAndDelete()
    {
        Services.GetRequiredService<CodexAppServerClient>().Dispose();
        Dispose();
        for (var attempt = 0; attempt < 20 && Directory.Exists(RootPath); attempt++)
        {
            try
            {
                Directory.Delete(RootPath, true);
            }
            catch (IOException) when (attempt < 19)
            {
                Thread.Sleep(50);
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var fakeAssembly = typeof(FakeCodexMarker).Assembly.Location;
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Gateway:StoragePath"] = StoragePath,
                ["Gateway:Limits:MaxConcurrent"] = "2",
                ["Gateway:Limits:MaxQueued"] = "2",
                ["Gateway:Limits:TimeoutMinutes"] = "1",
                ["Codex:ExecutablePath"] = "dotnet",
                ["Codex:ArgumentPrefix:0"] = fakeAssembly,
                ["Codex:ArgumentPrefix:1"] = "--scenario",
                ["Codex:ArgumentPrefix:2"] = ScenarioPath,
                ["Codex:Container:EngineExecutablePath"] = "dotnet",
                ["Codex:Container:EngineArgumentPrefix:0"] = fakeAssembly,
                ["Codex:Container:EngineArgumentPrefix:1"] = "--scenario",
                ["Codex:Container:EngineArgumentPrefix:2"] = ScenarioPath,
                ["Codex:Container:EngineArgumentPrefix:3"] = "--container-engine",
                ["Codex:Container:Image"] = "codex-gateway-runner:openai-compat",
                ["Codex:Container:Network"] = "codex-gateway-openai-compat",
                ["Codex:Container:MemoryMegabytes"] = "512",
                ["Codex:Container:CpuLimit"] = "1",
                ["Codex:Container:PidsLimit"] = "64",
                ["Codex:Container:TmpfsMegabytes"] = "64",
                ["Codex:HomePath"] = CodexHomePath,
                ["Codex:ModelCacheSeconds"] = "3600",
                ["AdminUi:Username"] = "test-admin",
                ["AdminUi:Password"] = "test-password"
            });
        });
    }
}
