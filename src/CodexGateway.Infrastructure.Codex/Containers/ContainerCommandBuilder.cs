using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using CodexGateway.Infrastructure.Storage;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Infrastructure.Codex;

internal static class ContainerCommandBuilder
{
    private const string WorkspaceTarget = "/workspace";
    private const string CodexHomeTarget = "/codex-home";
    private static readonly Regex EnvironmentVariablePattern = new(
        "^[A-Za-z_][A-Za-z0-9_]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex VolumeNamePattern = new(
        "^[A-Za-z0-9][A-Za-z0-9_.-]*$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly string[] ContainerPassthroughVariables =
    [
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
        "http_proxy", "https_proxy", "no_proxy", "all_proxy"
    ];

    internal static ProcessStartInfo CreateRunStartInfo(
        CodexContainerOptions options,
        string storageRoot,
        string codexHome,
        string engineWorkingDirectory,
        CodexRunRequest request,
        string containerName,
        string instanceId,
        IReadOnlyCollection<string> secretNames,
        IReadOnlyDictionary<string, string?> sourceEnvironment,
        string? containerUser,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections = null)
    {
        var image = string.IsNullOrWhiteSpace(request.RunnerImage)
            ? options.Image
            : request.RunnerImage;
        return CreateContainerStartInfo(
            options,
            storageRoot,
            codexHome,
            engineWorkingDirectory,
            request.Workspace.RootPath,
            containerName,
            instanceId,
            secretNames,
            sourceEnvironment,
            containerUser,
            gatewayConnections,
            mountCodexHome: true,
            workspaceReadOnly: false,
            CodexHomeTarget,
            arguments => AddCodexArguments(arguments, request, gatewayConnections),
            image: image);
    }

    internal static ProcessStartInfo CreateMcpDiscoveryStartInfo(
        CodexContainerOptions options,
        string storageRoot,
        string codexHome,
        string engineWorkingDirectory,
        RunWorkspace workspace,
        IReadOnlyList<ResolvedMcpServer> servers,
        string containerName,
        string instanceId,
        IReadOnlyCollection<string> secretNames,
        IReadOnlyDictionary<string, string?> sourceEnvironment,
        string? containerUser,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections = null)
    {
        return CreateContainerStartInfo(
            options,
            storageRoot,
            codexHome,
            engineWorkingDirectory,
            workspace.RootPath,
            containerName,
            instanceId,
            secretNames,
            sourceEnvironment,
            containerUser,
            gatewayConnections,
            mountCodexHome: false,
            workspaceReadOnly: true,
            CodexHomeTarget,
            arguments => AddMcpDiscoveryArguments(arguments, servers, gatewayConnections),
            image: options.Image);
    }

    private static ProcessStartInfo CreateContainerStartInfo(
        CodexContainerOptions options,
        string storageRoot,
        string codexHome,
        string engineWorkingDirectory,
        string workspaceRoot,
        string containerName,
        string instanceId,
        IReadOnlyCollection<string> secretNames,
        IReadOnlyDictionary<string, string?> sourceEnvironment,
        string? containerUser,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections,
        bool mountCodexHome,
        bool workspaceReadOnly,
        string containerHome,
        Action<ICollection<string>> addCodexArguments,
        string image)
    {
        ValidateConfiguration(options, storageRoot, codexHome);
        if (string.IsNullOrWhiteSpace(image) || image.StartsWith("-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The Codex runner image cannot be interpreted as an engine option.");
        }
        if (!VolumeNamePattern.IsMatch(containerName))
        {
            throw new InvalidOperationException("The generated container name is invalid.");
        }

        ContainerInstanceIdentity.Validate(instanceId);

        var safeSecretNames = secretNames
            .Where(name => EnvironmentVariablePattern.IsMatch(name))
            .Where(name => !CodexProcessEnvironment.IsReservedCredentialVariable(name))
            .Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToArray();
        var arguments = new List<string>
        {
            "run",
            "--interactive",
            "--name", containerName,
            "--label", ContainerRuntime.ManagedLabel,
            "--label", ContainerRuntime.RunIdLabelPrefix + containerName,
            "--label", ContainerRuntime.InstanceIdLabelPrefix + instanceId,
            "--init",
            "--read-only",
            "--workdir", WorkspaceTarget,
            "--network", options.Network,
            "--memory", options.MemoryMegabytes.ToString(CultureInfo.InvariantCulture) + "m",
            "--memory-swap", options.MemoryMegabytes.ToString(CultureInfo.InvariantCulture) + "m",
            "--cpus", options.CpuLimit.ToString(CultureInfo.InvariantCulture),
            "--pids-limit", options.PidsLimit.ToString(CultureInfo.InvariantCulture),
            "--tmpfs", $"/tmp:rw,nosuid,nodev,noexec,size={options.TmpfsMegabytes.ToString(CultureInfo.InvariantCulture)}m",
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--security-opt", "seccomp=unconfined"
        };
        if (!string.IsNullOrWhiteSpace(containerUser))
        {
            arguments.Add("--user");
            arguments.Add(containerUser);
        }

        arguments.Add("--mount");
        arguments.Add(BuildWorkspaceMount(options, storageRoot, workspaceRoot, workspaceReadOnly));
        if (mountCodexHome)
        {
            arguments.Add("--mount");
            arguments.Add(BuildAuthMount(options, codexHome));
        }
        else
        {
            arguments.Add("--tmpfs");
            arguments.Add(
                $"{CodexHomeTarget}:rw,nosuid,nodev,noexec,size={options.TmpfsMegabytes.ToString(CultureInfo.InvariantCulture)}m,mode=1777");
        }

        AddContainerEnvironment(arguments, "CODEX_HOME", containerHome);
        AddContainerEnvironment(arguments, "HOME", containerHome);
        AddContainerEnvironment(arguments, "USERPROFILE", containerHome);
        AddContainerEnvironment(arguments, "XDG_CONFIG_HOME", containerHome);
        AddContainerEnvironment(arguments, "XDG_CACHE_HOME", containerHome + "/.cache");
        AddContainerEnvironment(arguments, "TEMP", "/tmp");
        AddContainerEnvironment(arguments, "TMP", "/tmp");
        AddContainerEnvironment(arguments, "TMPDIR", "/tmp");

        foreach (var name in ContainerPassthroughVariables.Where(name => HasValue(sourceEnvironment, name)))
        {
            arguments.Add("--env");
            arguments.Add(name);
        }

        foreach (var name in safeSecretNames)
        {
            arguments.Add("--env");
            arguments.Add(name);
        }

        arguments.Add("--entrypoint");
        arguments.Add("codex");
        arguments.Add(image);
        addCodexArguments(arguments);

        return CreateEngineStartInfo(
            options,
            engineWorkingDirectory,
            arguments,
            safeSecretNames,
            sourceEnvironment,
            redirectStandardInput: true);
    }

    internal static ProcessStartInfo CreateEngineStartInfo(
        CodexContainerOptions options,
        string workingDirectory,
        IReadOnlyCollection<string> arguments,
        IReadOnlyCollection<string> secretNames,
        IReadOnlyDictionary<string, string?> sourceEnvironment,
        bool redirectStandardInput)
    {
        var info = new ProcessStartInfo
        {
            FileName = options.EngineExecutablePath,
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = redirectStandardInput,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        ContainerEngineEnvironment.Configure(info, sourceEnvironment, secretNames);
        foreach (var prefix in options.EngineArgumentPrefix)
        {
            info.ArgumentList.Add(prefix);
        }

        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        return info;
    }

    internal static void ValidateConfiguration(
        CodexContainerOptions options,
        string storageRoot,
        string codexHome)
    {
        if (string.IsNullOrWhiteSpace(options.EngineExecutablePath) ||
            string.IsNullOrWhiteSpace(options.Image) ||
            string.IsNullOrWhiteSpace(options.Network))
        {
            throw new InvalidOperationException("The Codex container runtime configuration is incomplete.");
        }

        if (options.Image.StartsWith("-", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Codex:Container:Image cannot be interpreted as an engine option.");
        }

        ValidateVolumeName(options.WorkspaceVolume, nameof(options.WorkspaceVolume));
        ValidateVolumeName(options.AuthVolume, nameof(options.AuthVolume));
        if (options.WorkspaceVolume is null)
        {
            ValidateMountValue(Path.GetFullPath(storageRoot), "Gateway storage path");
        }

        if (options.AuthVolume is null)
        {
            ValidateMountValue(Path.GetFullPath(codexHome), "Codex home path");
        }
    }

    private static string BuildWorkspaceMount(
        CodexContainerOptions options,
        string storageRoot,
        string workspaceRoot,
        bool readOnly)
    {
        var fullStorageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot));
        var fullWorkspaceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        if (options.WorkspaceVolume is null)
        {
            ValidateMountValue(fullWorkspaceRoot, "Run workspace path");
            return $"type=bind,source={fullWorkspaceRoot},target={WorkspaceTarget}" +
                   (readOnly ? ",readonly" : string.Empty);
        }

        var relativePath = Path.GetRelativePath(fullStorageRoot, fullWorkspaceRoot);
        if (relativePath == "." ||
            Path.IsPathRooted(relativePath) ||
            relativePath.Equals("..", StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The run workspace is outside Gateway:StoragePath.");
        }

        var volumeSubpath = relativePath.Replace(Path.DirectorySeparatorChar, '/');
        if (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar)
        {
            volumeSubpath = volumeSubpath.Replace(Path.AltDirectorySeparatorChar, '/');
        }

        ValidateMountValue(volumeSubpath, "Run workspace volume subpath");
        return $"type=volume,source={options.WorkspaceVolume},target={WorkspaceTarget},volume-subpath={volumeSubpath}" +
               (readOnly ? ",readonly" : string.Empty);
    }

    private static string BuildAuthMount(CodexContainerOptions options, string codexHome)
    {
        if (options.AuthVolume is not null)
        {
            return $"type=volume,source={options.AuthVolume},target={CodexHomeTarget}";
        }

        var fullCodexHome = Path.GetFullPath(codexHome);
        ValidateMountValue(fullCodexHome, "Codex home path");
        return $"type=bind,source={fullCodexHome},target={CodexHomeTarget}";
    }

    private static void AddContainerEnvironment(ICollection<string> arguments, string name, string value)
    {
        arguments.Add("--env");
        arguments.Add(name + "=" + value);
    }

    private static void ValidateVolumeName(string? value, string propertyName)
    {
        if (value is not null && !VolumeNamePattern.IsMatch(value))
        {
            throw new InvalidOperationException($"Codex:Container:{propertyName} is not a valid Docker volume name.");
        }
    }

    private static void ValidateMountValue(string value, string description)
    {
        if (value.Contains(',', StringComparison.Ordinal) ||
            value.Contains('\r', StringComparison.Ordinal) ||
            value.Contains('\n', StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{description} cannot contain commas or line breaks.");
        }
    }

    private static bool HasValue(IReadOnlyDictionary<string, string?> source, string name) =>
        source.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value);

    private static void AddCodexArguments(ICollection<string> arguments, CodexRunRequest request, IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections)
    {
        arguments.Add("exec");
        arguments.Add("--strict-config");
        arguments.Add("--ephemeral");
        arguments.Add("--ignore-user-config");
        arguments.Add("--ignore-rules");
        arguments.Add("--skip-git-repo-check");
        arguments.Add("--json");
        arguments.Add("--model");
        arguments.Add(request.Model);
        if (request.OutputSchema is not null)
        {
            arguments.Add("--output-schema");
            arguments.Add(WorkspaceManager.ContainerOutputSchemaPath);
        }

        arguments.Add("--cd");
        arguments.Add(WorkspaceTarget);
        arguments.Add("--disable");
        arguments.Add("apps");
        arguments.Add("--disable");
        arguments.Add("plugins");
        AddConfig(arguments, "approval_policy=\"never\"");
        AddConfig(arguments, "web_search=\"disabled\"");
        AddConfig(arguments, $"model_reasoning_effort={ContainerCodexRunner.TomlString(request.ReasoningEffort)}");
        AddConfig(arguments, $"default_permissions={ContainerCodexRunner.TomlString(ContainerCodexRunner.RunPermissionProfile)}");
        AddConfig(
            arguments,
            $"permissions.{ContainerCodexRunner.RunPermissionProfile}.filesystem=" +
            ContainerCodexRunner.BuildFileSystemPermissionTable());
        AddConfig(arguments, $"permissions.{ContainerCodexRunner.RunPermissionProfile}.network.enabled=false");
        AddConfig(arguments, "cli_auth_credentials_store=\"file\"");
        AddConfig(arguments, "shell_environment_policy.inherit=\"core\"");
        AddConfig(arguments, "shell_environment_policy.ignore_default_excludes=false");
        AddConfig(
            arguments,
            "shell_environment_policy.filters={\"PATH\"=\"include\",\"PATHEXT\"=\"include\",\"HOME\"=\"include\",\"USERPROFILE\"=\"include\",\"XDG_CONFIG_HOME\"=\"include\",\"XDG_CACHE_HOME\"=\"include\",\"TEMP\"=\"include\",\"TMP\"=\"include\",\"TMPDIR\"=\"include\",\"LANG\"=\"include\",\"LC_*\"=\"include\",\"TZ\"=\"include\",\"SYSTEMROOT\"=\"include\",\"COMSPEC\"=\"include\"}");
        AddConfig(
            arguments,
            "shell_environment_policy.set=" + ContainerCodexRunner.TomlTable(
                ("HOME", "/workspace/.home"),
                ("USERPROFILE", "/workspace/.home"),
                ("XDG_CONFIG_HOME", "/workspace/.home"),
                ("XDG_CACHE_HOME", "/workspace/.home/.cache"),
                ("TEMP", "/tmp"),
                ("TMP", "/tmp"),
                ("TMPDIR", "/tmp")));

        foreach (var server in request.McpServers)
        {
            GatewayMcpRunnerConnection? connection = null;
            if (gatewayConnections is not null)
            {
                gatewayConnections.TryGetValue(server.Definition.Id, out connection);
            }
            ContainerCodexRunner.AddMcpConfiguration(arguments, server, connection);
        }

        arguments.Add("-");
    }

    private static void AddMcpDiscoveryArguments(
        ICollection<string> arguments,
        IReadOnlyList<ResolvedMcpServer> servers,
        IReadOnlyDictionary<string, GatewayMcpRunnerConnection>? gatewayConnections)
    {
        arguments.Add("app-server");
        arguments.Add("--listen");
        arguments.Add("stdio://");
        arguments.Add("--strict-config");
        arguments.Add("--disable");
        arguments.Add("apps");
        arguments.Add("--disable");
        arguments.Add("plugins");
        AddConfig(arguments, "mcp_servers={}");
        AddConfig(arguments, "shell_environment_policy.inherit=\"core\"");
        AddConfig(arguments, "shell_environment_policy.ignore_default_excludes=false");
        AddConfig(
            arguments,
            "shell_environment_policy.filters={\"PATH\"=\"include\",\"PATHEXT\"=\"include\",\"HOME\"=\"include\",\"USERPROFILE\"=\"include\",\"XDG_CONFIG_HOME\"=\"include\",\"XDG_CACHE_HOME\"=\"include\",\"TEMP\"=\"include\",\"TMP\"=\"include\",\"TMPDIR\"=\"include\",\"LANG\"=\"include\",\"LC_*\"=\"include\",\"TZ\"=\"include\",\"SYSTEMROOT\"=\"include\",\"COMSPEC\"=\"include\"}");
        AddConfig(
            arguments,
            "shell_environment_policy.set=" + ContainerCodexRunner.TomlTable(
                ("HOME", CodexHomeTarget),
                ("USERPROFILE", CodexHomeTarget),
                ("XDG_CONFIG_HOME", CodexHomeTarget),
                ("XDG_CACHE_HOME", CodexHomeTarget + "/.cache"),
                ("TEMP", "/tmp"),
                ("TMP", "/tmp"),
                ("TMPDIR", "/tmp")));
        foreach (var server in servers)
        {
            GatewayMcpRunnerConnection? connection = null;
            if (gatewayConnections is not null)
            {
                gatewayConnections.TryGetValue(server.Definition.Id, out connection);
            }
            ContainerCodexRunner.AddMcpConfiguration(arguments, server, connection);
        }
    }

    internal static void AddConfig(ICollection<string> arguments, string value)
    {
        arguments.Add("--config");
        arguments.Add(value);
    }
}
