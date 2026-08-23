using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace CodexGateway.EndToEndTests;

public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    private readonly int _maxConcurrent;
    private readonly int _maxQueued;
    private readonly int? _timeoutSeconds;
    private readonly int? _appServerRequestTimeoutSeconds;
    private readonly int? _deviceLoginTimeoutSeconds;
    private readonly int? _maxArtifactFileMegabytes;
    private readonly int? _maxArtifactTotalMegabytes;
    private readonly bool _adminEnabled;

    public GatewayFactory(
        int maxConcurrent = 2,
        int maxQueued = 2,
        int? timeoutSeconds = null,
        int? appServerRequestTimeoutSeconds = null,
        int? deviceLoginTimeoutSeconds = null,
        int? maxArtifactFileMegabytes = null,
        int? maxArtifactTotalMegabytes = null,
        bool adminEnabled = true)
    {
        _maxConcurrent = maxConcurrent;
        _maxQueued = maxQueued;
        _timeoutSeconds = timeoutSeconds;
        _appServerRequestTimeoutSeconds = appServerRequestTimeoutSeconds;
        _deviceLoginTimeoutSeconds = deviceLoginTimeoutSeconds;
        _maxArtifactFileMegabytes = maxArtifactFileMegabytes;
        _maxArtifactTotalMegabytes = maxArtifactTotalMegabytes;
        _adminEnabled = adminEnabled;
        RootPath = Path.Combine(Path.GetTempPath(), "codex-gateway-e2e", Guid.NewGuid().ToString("N"));
        StoragePath = Path.Combine(RootPath, "data");
        ScenarioPath = Path.Combine(RootPath, "fake");
        CodexHomePath = Path.Combine(RootPath, "codex-home");
        Directory.CreateDirectory(StoragePath);
        Directory.CreateDirectory(ScenarioPath);
        Directory.CreateDirectory(CodexHomePath);
    }

    public string RootPath { get; }

    public string StoragePath { get; }

    public string ScenarioPath { get; }

    public string CodexHomePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var fakeAssembly = typeof(FakeCodexMarker).Assembly.Location;
        builder.UseStaticWebAssets();
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["Gateway:StoragePath"] = StoragePath,
                ["Gateway:ApiKeys:0:Id"] = "default",
                ["Gateway:ApiKeys:0:Name"] = "Default test key",
                ["Gateway:ApiKeys:0:Key"] = "e2e-api-key",
                ["Gateway:ApiKeys:1:Id"] = "secondary",
                ["Gateway:ApiKeys:1:Name"] = "Secondary test key",
                ["Gateway:ApiKeys:1:Key"] = "e2e-secondary-api-key",
                ["Gateway:Limits:MaxConcurrent"] = _maxConcurrent.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["Gateway:Limits:MaxQueued"] = _maxQueued.ToString(System.Globalization.CultureInfo.InvariantCulture),
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
                ["Codex:Container:Image"] = "codex-gateway-runner:e2e",
                ["Codex:Container:Network"] = "codex-gateway-e2e",
                ["Codex:Container:MemoryMegabytes"] = "512",
                ["Codex:Container:CpuLimit"] = "1",
                ["Codex:Container:PidsLimit"] = "64",
                ["Codex:Container:TmpfsMegabytes"] = "64",
                ["Codex:HomePath"] = CodexHomePath,
                ["Codex:ModelCacheSeconds"] = "3600",
                ["AdminUi:Enabled"] = _adminEnabled.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["AdminUi:Username"] = "test-admin",
                ["AdminUi:Password"] = "test-password"
            };
            if (_timeoutSeconds is { } timeoutSeconds)
            {
                settings["Gateway:Limits:TimeoutSeconds"] = timeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (_appServerRequestTimeoutSeconds is { } appServerRequestTimeoutSeconds)
            {
                settings["Codex:AppServerRequestTimeoutSeconds"] = appServerRequestTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (_deviceLoginTimeoutSeconds is { } deviceLoginTimeoutSeconds)
            {
                settings["Codex:DeviceLoginTimeoutSeconds"] = deviceLoginTimeoutSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (_maxArtifactFileMegabytes is { } maximumArtifactFileMegabytes)
            {
                settings["Gateway:Artifacts:MaxFileMegabytes"] = maximumArtifactFileMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            if (_maxArtifactTotalMegabytes is { } maximumArtifactTotalMegabytes)
            {
                settings["Gateway:Artifacts:MaxTotalMegabytes"] = maximumArtifactTotalMegabytes.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            configuration.AddInMemoryCollection(settings);
        });
    }
}
