using CodexGateway.Infrastructure.Codex;
using CodexGateway.Testing;
using CodexGateway.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.OpenAICompatibilityTests.Infrastructure;

public sealed class CompatibilityGatewayFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public CompatibilityGatewayFactory()
    {
        _connectionString = PostgreSqlTestDatabase.CreateConnectionString();
        RootPath = Path.Combine(Path.GetTempPath(), "codex-gateway-openai-compat", Guid.NewGuid().ToString("N"));
        StoragePath = Path.Combine(RootPath, "data");
        ScenarioPath = Path.Combine(RootPath, "fake");
        CodexHomePath = Path.Combine(RootPath, "codex-home");
        Directory.CreateDirectory(StoragePath);
        Directory.CreateDirectory(ScenarioPath);
        Directory.CreateDirectory(CodexHomePath);
        File.WriteAllText(Path.Combine(ScenarioPath, "authenticated"), string.Empty);
    }

    public string RootPath { get; }

    public string StoragePath { get; }

    public string ScenarioPath { get; }

    public string CodexHomePath { get; }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        var repository = host.Services.GetRequiredService<IRepository<GatewayState>>();
        repository.GetAndUpdateAsync(
                GatewayState.DocumentId,
                state => state.ApiKeys =
                [
                    new ApiKeyDefinition { Id = "default", Name = "Default test key", Key = "e2e-api-key" }
                ])
            .GetAwaiter()
            .GetResult();
        return host;
    }

    public void StopAndDelete()
    {
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
        builder.UseSetting("ConnectionStrings:Gateway", _connectionString);
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Gateway"] = _connectionString,
                ["Gateway:StoragePath"] = StoragePath,
                ["Gateway:Limits:MaxConcurrent"] = "2",
                ["Gateway:Limits:MaxQueued"] = "2",
                ["Gateway:Limits:TimeoutSeconds"] = "60",
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
                ["Codex:Models:0:Id"] = "gpt-test-sol",
                ["Codex:Models:0:Name"] = "GPT Test Sol",
                ["Codex:Models:0:SupportedReasoningEfforts:0"] = "low",
                ["Codex:Models:0:SupportedReasoningEfforts:1"] = "medium",
                ["Codex:Models:0:SupportedReasoningEfforts:2"] = "high",
                ["Codex:Models:0:DefaultReasoningEffort"] = "medium",
                ["AdminUi:Username"] = "test-admin",
                ["AdminUi:Password"] = "test-password"
            });
        });
    }
}
