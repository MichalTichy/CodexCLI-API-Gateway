using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using CodexGateway.Infrastructure.Storage;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex;

public sealed class ContainerRuntime
{
    internal const string ManagedLabel = "com.codex-gateway.managed=true";
    internal const string RunIdLabelPrefix = "com.codex-gateway.run-id=";
    internal const string InstanceIdLabelPrefix = "com.codex-gateway.instance-id=";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);
    private static readonly Regex RunWorkspaceNamePattern = new(
        "^run_[a-f0-9]{32}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly CodexOptions _options;
    private readonly StoragePaths _paths;
    private readonly string _codexHome;
    private readonly string _contentRoot;
    private readonly ILogger<ContainerRuntime> _logger;
    private string? _instanceId;

    public ContainerRuntime(
        IOptions<CodexOptions> options,
        StoragePaths paths,
        IHostEnvironment environment,
        ILogger<ContainerRuntime> logger)
    {
        _options = options.Value;
        _paths = paths;
        _contentRoot = environment.ContentRootPath;
        _codexHome = ResolvePath(_options.HomePath, _contentRoot);
        _logger = logger;
    }

    internal string CodexHome => _codexHome;

    internal ProcessStartInfo CreateRunStartInfo(CodexRunRequest request, string containerName, IReadOnlyDictionary<string, GatewayMcpRunnerConnection> gatewayConnections)
    {
        ValidateGatewayIdentity();
        Directory.CreateDirectory(_codexHome);
        Directory.CreateDirectory(Path.Combine(_codexHome, ".cache"));
        Directory.CreateDirectory(Path.Combine(request.Workspace.RootPath, ".home"));

        var secretNames = GetMcpSecretNames(request, gatewayConnections);
        return ContainerCommandBuilder.CreateRunStartInfo(
            _options.Container,
            _paths.Root,
            _codexHome,
            _contentRoot,
            request,
            containerName,
            GetInstanceId(),
            secretNames,
            GetEnvironmentSnapshot(gatewayConnections),
            UnixIdentity.GetContainerUser(),
            gatewayConnections);
    }

    internal ProcessStartInfo CreateMcpDiscoveryStartInfo(
        RunWorkspace workspace,
        IReadOnlyList<ResolvedMcpServer> servers,
        string containerName,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection> gatewayConnections)
    {
        ValidateGatewayIdentity();
        var secretNames = GetMcpSecretNames(servers, gatewayConnections);
        return ContainerCommandBuilder.CreateMcpDiscoveryStartInfo(
            _options.Container,
            _paths.Root,
            _codexHome,
            _contentRoot,
            workspace,
            servers,
            containerName,
            GetInstanceId(),
            secretNames,
            GetEnvironmentSnapshot(gatewayConnections),
            UnixIdentity.GetContainerUser(),
            gatewayConnections);
    }

    internal async Task PreflightAsync(CancellationToken cancellationToken)
    {
        ContainerCommandBuilder.ValidateConfiguration(
            _options.Container,
            _paths.Root,
            _codexHome);
        ValidateGatewayIdentity();
        var instanceId = GetInstanceId();

        var version = await ExecuteAsync(
            ["version", "--format", "{{.Server.Version}}"],
            CommandTimeout,
            cancellationToken);
        if (!version.Succeeded || string.IsNullOrWhiteSpace(version.StandardOutput))
        {
            throw new InvalidOperationException(
                "The configured container engine is unavailable or its daemon cannot be reached.");
        }

        var containers = await ExecuteAsync(
            [
                "ps", "--all", "--quiet",
                "--filter", $"label={ManagedLabel}",
                "--filter", $"label={InstanceIdLabelPrefix}{instanceId}"
            ],
            CommandTimeout,
            cancellationToken);
        if (!containers.Succeeded)
        {
            throw new InvalidOperationException("Managed Codex containers could not be enumerated during startup.");
        }

        var orphanIds = containers.StandardOutput
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (var batch in orphanIds.Chunk(50))
        {
            var arguments = new List<string> { "rm", "--force" };
            arguments.AddRange(batch);
            var cleanup = await ExecuteAsync(arguments, CommandTimeout, cancellationToken);
            if (!cleanup.Succeeded && !IsMissingContainer(cleanup.StandardError))
            {
                throw new InvalidOperationException("An orphaned managed Codex container could not be removed.");
            }
        }

        CleanupStaleRunWorkspaces(_paths.Runs);

        var image = await ExecuteAsync(
            ["image", "inspect", _options.Container.Image],
            CommandTimeout,
            cancellationToken);
        if (!image.Succeeded)
        {
            throw new InvalidOperationException(
                $"The configured Codex runner image '{_options.Container.Image}' is unavailable.");
        }

        _logger.LogInformation(
            "Container runtime preflight succeeded for Codex runner image {Image}.",
            _options.Container.Image);
    }

    internal async Task<bool> ForceRemoveAsync(string containerName, bool retryMissingContainer)
    {
        try
        {
            ContainerCommandResult? lastResult = null;
            var maximumAttempts = retryMissingContainer ? 4 : 1;
            for (var attempt = 0; attempt < maximumAttempts; attempt++)
            {
                lastResult = await ExecuteAsync(
                    ["rm", "--force", containerName],
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None);
                if (lastResult.Succeeded)
                {
                    return true;
                }

                if (!IsMissingContainer(lastResult.StandardError) &&
                    !IsRemovalInProgress(lastResult.StandardError))
                {
                    break;
                }

                if (attempt < maximumAttempts - 1)
                {
                    // A docker client can be cancelled while the daemon is still reserving the
                    // requested name. Rechecking closes that create/remove race without using IDs
                    // or secret-bearing shell interpolation.
                    await Task.Delay(TimeSpan.FromMilliseconds(100 * (1 << attempt)));
                }
            }

            if (lastResult is not null && IsMissingContainer(lastResult.StandardError))
            {
                return true;
            }

            _logger.LogWarning(
                "The managed Codex container {ContainerName} could not be force-removed.",
                containerName);
            return false;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The managed Codex container {ContainerName} could not be force-removed.",
                containerName);
            return false;
        }
    }

    internal async Task<ContainerCommandResult> ExecuteAsync(
        IReadOnlyCollection<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = ContainerCommandBuilder.CreateEngineStartInfo(
            _options.Container,
            _contentRoot,
            arguments,
            [],
            GetEnvironmentSnapshot(),
            redirectStandardInput: false);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                return new ContainerCommandResult(-1, string.Empty, "The container-engine process did not start.");
            }
        }
        catch (Exception exception)
        {
            return new ContainerCommandResult(-1, string.Empty, exception.Message);
        }

        var outputTask = ReadLimitedAsync(process.StandardOutput, 64 * 1024);
        var errorTask = ReadLimitedAsync(process.StandardError, 64 * 1024);
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutCancellation.Token);
        try
        {
            await process.WaitForExitAsync(linkedCancellation.Token);
        }
        catch (OperationCanceledException) when (timeoutCancellation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            KillProcessTree(process);
            await ObserveAsync(outputTask);
            await ObserveAsync(errorTask);
            return new ContainerCommandResult(-1, string.Empty, "The container-engine command timed out.");
        }
        catch
        {
            KillProcessTree(process);
            await ObserveAsync(outputTask);
            await ObserveAsync(errorTask);
            throw;
        }

        return new ContainerCommandResult(
            process.ExitCode,
            await outputTask,
            await errorTask);
    }

    internal static string CreateContainerName() =>
        "codex-gateway-run-" + Guid.NewGuid().ToString("N");

    internal static void CleanupStaleRunWorkspaces(string runsPath)
    {
        FileAttributes runsAttributes;
        try
        {
            runsAttributes = File.GetAttributes(runsPath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return;
        }

        if ((runsAttributes & FileAttributes.ReparsePoint) != 0 ||
            (runsAttributes & FileAttributes.Directory) == 0)
        {
            throw new InvalidOperationException("The gateway runs storage path is unsafe.");
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(
                     runsPath,
                     "run_*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(entry);
            if (!RunWorkspaceNamePattern.IsMatch(name))
            {
                throw new InvalidOperationException("An unrecognized run workspace prevents safe startup cleanup.");
            }

            // WorkspaceManager.Delete walks without following reparse points and fails closed on
            // special/unsafe entries. At this point the instance-scoped containers are already gone.
            WorkspaceManager.DeleteWorkspace(new RunWorkspace(entry, Path.Combine(entry, "artifacts"), null));
        }
    }

    private string GetInstanceId() =>
        _instanceId ??= ContainerInstanceIdentity.GetOrCreate(_paths.Root);

    private static void ValidateGatewayIdentity()
    {
        if (UnixIdentity.IsRoot())
        {
            throw new InvalidOperationException(
                "The gateway must run as a non-root user so run workspaces and Codex auth remain writable by the non-root runner.");
        }
    }

    internal static void KillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
                process.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    private static string[] GetMcpSecretNames(
        CodexRunRequest request,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection> gatewayConnections) =>
        GetMcpSecretNames(request.McpServers, gatewayConnections);

    private static string[] GetMcpSecretNames(
        IEnumerable<ResolvedMcpServer> servers,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection> gatewayConnections) =>
        servers.SelectMany(server =>
        {
            IEnumerable<string?> names;
            if (server.Definition.ExecutionMode == CodexGateway.Models.McpExecutionMode.Gateway &&
                gatewayConnections.TryGetValue(server.Definition.Id, out var connection))
            {
                names = [connection.BearerTokenEnvironmentVariable];
            }
            else
            {
                names = server.Definition switch
                {
                    HttpMcpServerDefinition http => server.Definition.EnvironmentVariables
                        .Cast<string?>()
                        .Concat(Enumerable.Repeat(http.BearerTokenEnvironmentVariable, 1)),
                    StdioMcpServerDefinition => server.Definition.EnvironmentVariables,
                    _ => []
                };
            }

            return names;
        })
        .Where(variable => !string.IsNullOrWhiteSpace(variable))
        .Cast<string>()
        .Where(variable => !CodexProcessEnvironment.IsReservedCredentialVariable(variable))
        .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
        .ToArray();

    internal static IReadOnlyDictionary<string, string?> GetEnvironmentSnapshot(
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections = null)
    {
        var environment = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string?)entry.Value,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var connection in gatewayConnections?.Values ?? [])
        {
            environment[connection.BearerTokenEnvironmentVariable] = connection.BearerToken;
        }

        return environment;
    }

    private static bool IsMissingContainer(string diagnostics) =>
        diagnostics.Contains("No such container", StringComparison.OrdinalIgnoreCase);

    private static bool IsRemovalInProgress(string diagnostics) =>
        diagnostics.Contains("removal of container", StringComparison.OrdinalIgnoreCase) &&
        diagnostics.Contains("already in progress", StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                break;
            }

            if (result.Length < maximumCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, maximumCharacters - result.Length));
            }
        }

        return result.ToString();
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
    }

    private static string ResolvePath(string configured, string contentRoot) => Path.GetFullPath(
        Path.IsPathRooted(configured) ? configured : Path.Combine(contentRoot, configured));
}
