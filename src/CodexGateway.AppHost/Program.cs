var builder = DistributedApplication.CreateBuilder(args);

var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", ".."));
var dataPath = ResolvePath(
    "CODEX_GATEWAY_ASPIRE_DATA_PATH",
    Path.Combine(repositoryRoot, ".aspire", "data"));
var codexHomePath = ResolvePath(
    "CODEX_GATEWAY_ASPIRE_CODEX_HOME_PATH",
    Path.Combine(repositoryRoot, ".aspire", "codex-home"));
Directory.CreateDirectory(dataPath);
Directory.CreateDirectory(codexHomePath);

// Aspire runs the gateway project on the host. Leaving the two container volume
// settings unset selects bind mounts for these absolute development paths.
var runnerImage = builder.AddDockerfile(
        "codex-runner-image",
        repositoryRoot,
        "Dockerfile",
        "runner")
    .WithImage("codex-gateway-runner", "0.148.0")
    .WithEntrypoint("/bin/true");

builder.AddProject<Projects.CodexGateway_App>("gateway")
    .WithEnvironment("Gateway__StoragePath", dataPath)
    .WithEnvironment("Codex__HomePath", codexHomePath)
    .WithEnvironment("Codex__Container__EngineExecutablePath", "docker")
    .WithEnvironment("Codex__Container__Image", "codex-gateway-runner:0.148.0")
    .WithHttpEndpoint(port: 5050, name: "http")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WaitForCompletion(runnerImage);

builder.Build().Run();

static string ResolvePath(string variableName, string defaultPath)
{
    var configuredPath = Environment.GetEnvironmentVariable(variableName);
    return Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath) ? defaultPath : configuredPath);
}
