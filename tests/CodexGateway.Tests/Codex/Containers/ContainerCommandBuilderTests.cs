using CodexGateway.Infrastructure.Codex;
using CodexGateway.Infrastructure.FileStorage;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.McpServers;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using System.Text.Json;

namespace CodexGateway.Tests.Codex.Containers;

public sealed class ContainerCommandBuilderTests
{
    [Fact]
    public void Run_command_uses_hardened_container_and_bind_mounts_without_secret_values()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_abc");
        var codexHome = Path.GetFullPath(Path.Combine("test-data", "codex-home"));
        var request = CreateRequest(workspaceRoot);
        var options = new CodexContainerOptions
        {
            EngineExecutablePath = "docker-test",
            EngineArgumentPrefix = ["--context", "gateway"],
            Image = "runner:test",
            Network = "mcp-network"
        };
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "/safe/bin",
            ["MCP_TOKEN"] = "mcp-secret-value",
            ["ConnectionStrings__Gateway"] = "gateway-secret-value",
            ["OPENAI_API_KEY"] = "provider-secret-value",
            ["UNRELATED_SECRET"] = "unrelated-secret-value"
        };

        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            options,
            storageRoot,
            codexHome,
            storageRoot,
            request,
            "codex-gateway-run-abc",
            "0123456789abcdef0123456789abcdef",
            ["MCP_TOKEN", "OPENAI_API_KEY"],
            source,
            "10001:10001");
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Equal("docker-test", startInfo.FileName);
        Assert.Equal(["--context", "gateway", "run"], arguments[..3]);
        AssertArgumentPair(arguments, "--name", "codex-gateway-run-abc");
        AssertArgumentPair(arguments, "--label", ContainerRuntime.ManagedLabel);
        AssertArgumentPair(
            arguments,
            "--label",
            ContainerRuntime.InstanceIdLabelPrefix + "0123456789abcdef0123456789abcdef");
        AssertArgumentPair(arguments, "--workdir", "/workspace");
        AssertArgumentPair(arguments, "--network", "mcp-network");
        AssertArgumentPair(arguments, "--memory", "2048m");
        AssertArgumentPair(arguments, "--memory-swap", "2048m");
        AssertArgumentPair(arguments, "--cpus", "2");
        AssertArgumentPair(arguments, "--pids-limit", "256");
        AssertArgumentPair(arguments, "--cap-drop", "ALL");
        AssertArgumentPair(arguments, "--security-opt", "no-new-privileges");
        AssertArgumentPair(arguments, "--security-opt", "seccomp=unconfined");
        AssertArgumentPair(arguments, "--user", "10001:10001");
        Assert.Contains("--read-only", arguments);
        Assert.Contains("--init", arguments);
        Assert.DoesNotContain("--rm", arguments);
        Assert.Contains("/tmp:rw,nosuid,nodev,noexec,size=256m", arguments);
        Assert.Contains($"type=bind,source={workspaceRoot},target=/workspace", arguments);
        Assert.Contains($"type=bind,source={codexHome},target=/codex-home", arguments);
        AssertArgumentPair(arguments, "--entrypoint", "codex");
        Assert.Contains("runner:test", arguments);
        Assert.Contains(
            Enumerable.Range(0, arguments.Length - 1),
            index => arguments[index] == "runner:test" &&
                     arguments[index + 1] == "exec");
        Assert.Contains("MCP_TOKEN", arguments);
        Assert.DoesNotContain("OPENAI_API_KEY", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("mcp-secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("gateway-secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains("provider-secret-value", StringComparison.Ordinal));
        Assert.DoesNotContain(arguments, argument => argument.Contains(request.Prompt, StringComparison.Ordinal));
        Assert.Equal("mcp-secret-value", startInfo.Environment["MCP_TOKEN"]);
        Assert.False(startInfo.Environment.ContainsKey("ConnectionStrings__Gateway"));
        Assert.False(startInfo.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.False(startInfo.Environment.ContainsKey("UNRELATED_SECRET"));
    }

    [Fact]
    public void Run_command_can_omit_no_new_privileges_for_incompatible_hosts()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_compatibility");
        var options = new CodexContainerOptions
        {
            EngineExecutablePath = "docker-test",
            Image = "runner:test",
            Network = "mcp-network",
            NoNewPrivileges = false
        };

        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            options,
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            CreateRequest(workspaceRoot),
            "codex-gateway-run-compatibility",
            "0123456789abcdef0123456789abcdef",
            [],
            new Dictionary<string, string?>(),
            "10001:10001");
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.DoesNotContain("no-new-privileges", arguments);
        AssertArgumentPair(arguments, "--security-opt", "seccomp=unconfined");
        AssertArgumentPair(arguments, "--cap-drop", "ALL");
        Assert.Contains("--read-only", arguments);
    }

    [Fact]
    public void Structured_run_passes_only_the_fixed_staged_schema_path_to_codex()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_structured");
        using var schemaDocument = JsonDocument.Parse(
            """
            {
              "type": "object",
              "properties": { "answer": { "type": "string" } },
              "required": ["answer"]
            }
            """);
        var request = CreateRequest(workspaceRoot) with
        {
            OutputSchema = schemaDocument.RootElement.Clone()
        };

        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            new CodexContainerOptions
            {
                EngineExecutablePath = "docker-test",
                Image = "runner:test"
            },
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            request,
            "codex-gateway-run-structured",
            "0123456789abcdef0123456789abcdef",
            [],
            new Dictionary<string, string?>(),
            null);
        var arguments = startInfo.ArgumentList.ToArray();

        AssertArgumentPair(arguments, "--output-schema", WorkspaceManager.ContainerOutputSchemaPath);
        Assert.DoesNotContain(
            arguments,
            argument => argument.Contains("\"properties\"", StringComparison.Ordinal) ||
                        argument.Contains("\"answer\"", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_command_uses_the_project_runner_image_when_supplied()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_project_image");
        var request = CreateRequest(workspaceRoot) with { RunnerImage = "project-runner:test" };

        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            new CodexContainerOptions { EngineExecutablePath = "docker-test", Image = "default-runner:test" },
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            request,
            "codex-gateway-run-project-image",
            "0123456789abcdef0123456789abcdef",
            [],
            new Dictionary<string, string?>(),
            null);

        var arguments = startInfo.ArgumentList.ToArray();
        Assert.Contains("project-runner:test", arguments);
        Assert.DoesNotContain("default-runner:test", arguments);
    }

    [Fact]
    public void Stdio_mcp_command_and_arguments_are_preserved_without_shell_interpolation()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_stdio");
        var server = new StdioMcpServerDefinition
        {
            Id = "stdio-mcp",
            Name = "STDIO MCP",
            Command = "/opt/mcp/server",
            Arguments = ["--stdio", "argument with spaces"],
            EnvironmentVariables = ["MCP_STDIO_TOKEN"],
            AvailableTools = ["read"]
        };
        var request = new CodexRunRequest(
            "test prompt",
            "gpt-test",
            "medium",
            new RunWorkspace(workspaceRoot, Path.Combine(workspaceRoot, "artifacts"), null),
            [new ResolvedMcpServer(server, ["read"], true)]);
        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            new CodexContainerOptions(),
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            request,
            "codex-gateway-run-stdio",
            "0123456789abcdef0123456789abcdef",
            ["MCP_STDIO_TOKEN"],
            new Dictionary<string, string?> { ["MCP_STDIO_TOKEN"] = "stdio-secret" },
            null);
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Contains(
            "mcp_servers.stdio_mcp.command=\"/opt/mcp/server\"",
            arguments);
        Assert.Contains(
            "mcp_servers.stdio_mcp.args=[\"--stdio\",\"argument with spaces\"]",
            arguments);
        Assert.Contains("MCP_STDIO_TOKEN", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("stdio-secret", StringComparison.Ordinal));
    }


    [Fact]
    public void Gateway_mcp_uses_scoped_connection_values_in_toml_and_container_env()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_gateway");
        var server = new HttpMcpServerDefinition
        {
            Id = "gateway-mcp",
            Name = "Gateway MCP",
            ExecutionMode = McpExecutionMode.Gateway,
            Url = "https://upstream.example.test/mcp",
            EnvironmentHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = "UPSTREAM_TOKEN"
            },
            EnvironmentVariables = ["UPSTREAM_EXTRA"],
            AvailableTools = ["read"]
        };
        var request = new CodexRunRequest(
            "test prompt",
            "gpt-test",
            "medium",
            new RunWorkspace(workspaceRoot, Path.Combine(workspaceRoot, "artifacts"), null),
            [new ResolvedMcpServer(server, ["read"], true)]);
        var connection = new GatewayMcpRunnerConnection(
            "http://gateway.local/mcp/gateway-mcp",
            "GATEWAY_TOKEN",
            "scoped-token-value");
        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            new CodexContainerOptions { EngineExecutablePath = "docker-test", Image = "runner:test" },
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            request,
            "codex-gateway-run-gateway",
            "0123456789abcdef0123456789abcdef",
            ["GATEWAY_TOKEN"],
            new Dictionary<string, string?> { ["GATEWAY_TOKEN"] = "scoped-token-value" },
            null,
            new Dictionary<string, GatewayMcpRunnerConnection> { ["gateway-mcp"] = connection });
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Contains("mcp_servers.gateway_mcp.url=\"http://gateway.local/mcp/gateway-mcp\"", arguments);
        Assert.Contains("mcp_servers.gateway_mcp.bearer_token_env_var=\"GATEWAY_TOKEN\"", arguments);
        Assert.Contains("GATEWAY_TOKEN", arguments);
        Assert.DoesNotContain("UPSTREAM_TOKEN", arguments);
        Assert.DoesNotContain("UPSTREAM_EXTRA", arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains("scoped-token-value", StringComparison.Ordinal));
        Assert.Equal("scoped-token-value", startInfo.Environment["GATEWAY_TOKEN"]);
    }

    [Fact]
    public void Mcp_discovery_uses_an_ephemeral_home_read_only_workspace_and_only_selected_secrets()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_discovery");
        var codexHome = Path.Combine(storageRoot, "auth-that-must-not-be-mounted");
        var server = new HttpMcpServerDefinition
        {
            Id = "metadata-mcp",
            Name = "Metadata MCP",
            Url = "https://mcp.example.test",
            EnvironmentHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = "MCP_TOKEN"
            },
            EnvironmentVariables = ["MCP_EXTRA"],
            AvailableTools = ["lookup", "write"]
        };
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "/safe/bin",
            ["MCP_TOKEN"] = "header-secret",
            ["MCP_EXTRA"] = "extra-secret",
            ["OPENAI_API_KEY"] = "provider-secret",
            ["ConnectionStrings__Gateway"] = "gateway-secret"
        };

        var startInfo = ContainerCommandBuilder.CreateMcpDiscoveryStartInfo(
            new CodexContainerOptions
            {
                EngineExecutablePath = "docker-test",
                Image = "runner:test",
                Network = "mcp-network"
            },
            storageRoot,
            codexHome,
            storageRoot,
            new RunWorkspace(workspaceRoot, Path.Combine(workspaceRoot, "artifacts"), null),
            [new ResolvedMcpServer(server, ["lookup"], Required: false)],
            "codex-gateway-run-discovery",
            "0123456789abcdef0123456789abcdef",
            ["MCP_TOKEN", "MCP_EXTRA", "OPENAI_API_KEY"],
            source,
            "10001:10001");
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Contains(
            $"type=bind,source={workspaceRoot},target=/workspace,readonly",
            arguments);
        Assert.DoesNotContain(arguments, argument => argument.Contains(codexHome, StringComparison.Ordinal));
        AssertArgumentPair(arguments, "--network", "mcp-network");
        AssertArgumentPair(arguments, "--user", "10001:10001");
        AssertArgumentPair(arguments, "--entrypoint", "codex");
        Assert.Contains("app-server", arguments);
        AssertArgumentPair(arguments, "--listen", "stdio://");
        Assert.Contains("--strict-config", arguments);
        Assert.Contains("--read-only", arguments);
        Assert.Contains("mcp_servers={}", arguments);
        Assert.DoesNotContain(
            arguments,
            argument => argument.StartsWith("mcp_servers.metadata_mcp.enabled_tools=", StringComparison.Ordinal));
        Assert.Contains(
            "mcp_servers.metadata_mcp.env_http_headers={\"X-Api-Key\"=\"MCP_TOKEN\"}",
            arguments);
        Assert.Contains("CODEX_HOME=/codex-home", arguments);
        Assert.Contains("HOME=/codex-home", arguments);
        Assert.Contains(
            "/codex-home:rw,nosuid,nodev,noexec,size=256m,mode=1777",
            arguments);
        Assert.Contains("MCP_TOKEN", arguments);
        Assert.Contains("MCP_EXTRA", arguments);
        Assert.DoesNotContain("OPENAI_API_KEY", arguments);
        Assert.Equal("header-secret", startInfo.Environment["MCP_TOKEN"]);
        Assert.Equal("extra-secret", startInfo.Environment["MCP_EXTRA"]);
        Assert.False(startInfo.Environment.ContainsKey("OPENAI_API_KEY"));
        Assert.False(startInfo.Environment.ContainsKey("ConnectionStrings__Gateway"));
        Assert.DoesNotContain(
            arguments,
            argument => argument.Contains("header-secret", StringComparison.Ordinal) ||
                        argument.Contains("extra-secret", StringComparison.Ordinal) ||
                        argument.Contains("provider-secret", StringComparison.Ordinal) ||
                        argument.Contains("gateway-secret", StringComparison.Ordinal));
    }

    [Fact]
    public void Named_workspace_volume_uses_storage_relative_volume_subpath()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var workspaceRoot = Path.Combine(storageRoot, "runs", "run_xyz");
        var options = new CodexContainerOptions
        {
            WorkspaceVolume = "gateway-data",
            AuthVolume = "gateway-auth"
        };

        var startInfo = ContainerCommandBuilder.CreateRunStartInfo(
            options,
            storageRoot,
            Path.Combine(storageRoot, "ignored-auth-bind"),
            storageRoot,
            CreateRequest(workspaceRoot),
            "codex-gateway-run-xyz",
            "0123456789abcdef0123456789abcdef",
            [],
            new Dictionary<string, string?>(),
            null);
        var arguments = startInfo.ArgumentList.ToArray();

        Assert.Contains(
            "type=volume,source=gateway-data,target=/workspace,volume-subpath=runs/run_xyz",
            arguments);
        Assert.Contains("type=volume,source=gateway-auth,target=/codex-home", arguments);
    }

    [Fact]
    public void Workspace_outside_storage_volume_is_rejected()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var outside = Path.GetFullPath(Path.Combine("test-data", "outside", "run"));
        var options = new CodexContainerOptions { WorkspaceVolume = "gateway-data" };

        var exception = Assert.Throws<InvalidOperationException>(() => ContainerCommandBuilder.CreateRunStartInfo(
            options,
            storageRoot,
            Path.Combine(storageRoot, "auth"),
            storageRoot,
            CreateRequest(outside),
            "codex-gateway-run-outside",
            "0123456789abcdef0123456789abcdef",
            [],
            new Dictionary<string, string?>(),
            null));

        Assert.Contains("outside Gateway:StoragePath", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Mount_paths_with_csv_delimiters_are_rejected()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway,data"));
        var options = new CodexContainerOptions();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContainerCommandBuilder.ValidateConfiguration(options, storageRoot, Path.Combine(storageRoot, "auth")));

        Assert.Contains("cannot contain commas", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_name_cannot_be_interpreted_as_a_container_engine_option()
    {
        var storageRoot = Path.GetFullPath(Path.Combine("test-data", "gateway-data"));
        var options = new CodexContainerOptions { Image = "--privileged" };

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ContainerCommandBuilder.ValidateConfiguration(options, storageRoot, Path.Combine(storageRoot, "auth")));

        Assert.Contains("cannot be interpreted", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Engine_environment_contains_only_connection_values_and_selected_mcp_secrets()
    {
        var source = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["PATH"] = "/safe/bin",
            ["DOCKER_HOST"] = "unix:///run/user/1000/docker.sock",
            ["DOCKER_CONTEXT"] = "rootless",
            ["MCP_TOKEN"] = "mcp-secret",
            ["ConnectionStrings__Gateway"] = "gateway-secret",
            ["OPENAI_API_KEY"] = "provider-secret",
            ["OTHER_TOKEN"] = "other-secret"
        };

        var environment = ContainerEngineEnvironment.Build(source, ["MCP_TOKEN", "OPENAI_API_KEY"]);

        Assert.Equal("/safe/bin", environment["PATH"]);
        Assert.Equal("unix:///run/user/1000/docker.sock", environment["DOCKER_HOST"]);
        Assert.Equal("rootless", environment["DOCKER_CONTEXT"]);
        Assert.Equal("mcp-secret", environment["MCP_TOKEN"]);
        Assert.DoesNotContain("ConnectionStrings__Gateway", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("OPENAI_API_KEY", environment.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("OTHER_TOKEN", environment.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Instance_identity_is_created_once_and_reused_for_the_same_storage_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-gateway-instance-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var first = ContainerInstanceIdentity.GetOrCreate(root);
            var second = ContainerInstanceIdentity.GetOrCreate(root);

            Assert.Equal(first, second);
            Assert.Matches("^[a-f0-9]{32}$", first);
            Assert.Equal(first, File.ReadAllText(Path.Combine(root, ".codex-gateway-instance")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void Invalid_persisted_instance_identity_is_rejected()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-gateway-instance-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, ".codex-gateway-instance"), "not-a-valid-identity");

            Assert.Throws<InvalidOperationException>(() => ContainerInstanceIdentity.GetOrCreate(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_cleanup_removes_only_recognized_stale_run_workspaces()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-gateway-run-cleanup-test-" + Guid.NewGuid().ToString("N"));
        var runs = Path.Combine(root, "runs");
        var staleRun = Path.Combine(runs, "run_0123456789abcdef0123456789abcdef");
        var unrelated = Path.Combine(runs, "keep-this-directory");
        Directory.CreateDirectory(Path.Combine(staleRun, "artifacts"));
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(staleRun, "artifact.txt"), "stale");
        try
        {
            ContainerRuntime.CleanupStaleRunWorkspaces(runs);

            Assert.False(Directory.Exists(staleRun));
            Assert.True(Directory.Exists(unrelated));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Startup_cleanup_fails_closed_for_unrecognized_run_entries()
    {
        var root = Path.Combine(Path.GetTempPath(), "codex-gateway-run-cleanup-test-" + Guid.NewGuid().ToString("N"));
        var runs = Path.Combine(root, "runs");
        var unrecognized = Path.Combine(runs, "run_not-a-gateway-id");
        Directory.CreateDirectory(unrecognized);
        try
        {
            Assert.Throws<InvalidOperationException>(() => ContainerRuntime.CleanupStaleRunWorkspaces(runs));
            Assert.True(Directory.Exists(unrecognized));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static CodexRunRequest CreateRequest(string workspaceRoot)
    {
        var server = new HttpMcpServerDefinition
        {
            Id = "test-mcp",
            Name = "Test MCP",
            Url = "https://mcp.example.test",
            EnvironmentHeaders = new Dictionary<string, string>
            {
                ["X-Api-Key"] = "MCP_TOKEN"
            },
            AvailableTools = ["read"]
        };
        return new CodexRunRequest(
            "prompt-that-must-only-use-stdin",
            "gpt-test",
            "high",
            new RunWorkspace(workspaceRoot, Path.Combine(workspaceRoot, "artifacts"), null),
            [new ResolvedMcpServer(server, ["read"], true)]);
    }

    private static void AssertArgumentPair(string[] arguments, string name, string value)
    {
        Assert.Contains(
            Enumerable.Range(0, arguments.Length - 1),
            index => arguments[index] == name && arguments[index + 1] == value);
    }
}
