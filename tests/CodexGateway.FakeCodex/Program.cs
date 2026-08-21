using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var arguments = args.ToList();
var scenarioRoot = ReadOption(arguments, "--scenario") ?? Path.Combine(Path.GetTempPath(), "codex-gateway-fake");
Directory.CreateDirectory(scenarioRoot);
var mode = arguments.Contains("--container-engine", StringComparer.Ordinal) ? "container-engine" :
    arguments.Contains("app-server", StringComparer.Ordinal) ? "app-server" :
    arguments.Contains("exec", StringComparer.Ordinal) ? "exec" : "unknown";

switch (mode)
{
    case "container-engine":
        await RunContainerEngineAsync();
        break;
    case "app-server":
        await RunAppServerAsync(ReadConfiguredMcpServers(arguments));
        break;
    case "exec":
        await RunExecAsync(arguments);
        break;
    default:
        Console.Error.WriteLine("Fake Codex expected exec or app-server mode.");
        Environment.ExitCode = 2;
        break;
}

async Task RunContainerEngineAsync()
{
    var markerIndex = arguments.IndexOf("--container-engine");
    var engineArguments = arguments.Skip(markerIndex + 1).ToArray();
    if (engineArguments.Length == 0)
    {
        Console.Error.WriteLine("Fake container engine expected a command.");
        Environment.ExitCode = 2;
        return;
    }

    if (engineArguments[0] != "run")
    {
        await RecordInvocationAsync("container-engine", string.Empty, engineArguments);
    }
    switch (engineArguments[0])
    {
        case "version":
            Console.WriteLine("24.0.0");
            return;
        case "info":
            Console.WriteLine("Fake container engine");
            return;
        case "image" when engineArguments.Length > 1 && engineArguments[1] == "inspect":
        case "inspect":
            Console.WriteLine("[]");
            return;
        case "ps":
            await ListFakeContainersAsync();
            return;
        case "rm":
            await RemoveFakeContainersAsync(engineArguments);
            return;
        case "run":
            await RunContainerAsync(engineArguments);
            return;
        default:
            Console.Error.WriteLine($"Unsupported fake container-engine command '{engineArguments[0]}'.");
            Environment.ExitCode = 2;
            return;
    }
}

async Task RunContainerAsync(IReadOnlyList<string> engineArguments)
{
    var parsed = ParseContainerRun(engineArguments);
    var workspace = ResolveContainerPath(parsed.WorkingDirectory, parsed.Mounts);
    if (workspace is null || !Directory.Exists(workspace))
    {
        Console.Error.WriteLine("Fake container run requires a mounted working directory.");
        Environment.ExitCode = 2;
        return;
    }

    var containerName = ReadOption(engineArguments, "--name") ??
                        throw new InvalidOperationException("Fake container run requires --name.");
    _ = await RegisterFakeContainerAsync(containerName);
    await RecordInvocationAsync("container-engine", string.Empty, engineArguments);
    Directory.SetCurrentDirectory(workspace);
    foreach (var (name, value) in parsed.Environment)
    {
        Environment.SetEnvironmentVariable(name, ResolveEnvironmentValue(value, parsed.Mounts));
    }

    // Like a real container started without --rm, its state remains until the
    // gateway issues the explicit force-removal command.
    if (parsed.CommandArguments.Contains("app-server", StringComparer.Ordinal))
    {
        await RunAppServerAsync(ReadConfiguredMcpServers(parsed.CommandArguments));
    }
    else
    {
        await RunExecAsync(parsed.CommandArguments);
    }
}

async Task RunExecAsync(IReadOnlyList<string> codexArguments)
{
    var prompt = await Console.In.ReadToEndAsync();
    await RecordInvocationAsync("exec", prompt, codexArguments);
    var gateName = ReadScenarioValue(prompt, "gate");
    if (gateName is not null)
    {
        await WaitForGateReleaseAsync(gateName);
    }

    var workspaceBytesText = ReadScenarioValue(prompt, "workspace-bytes");
    if (workspaceBytesText is not null &&
        long.TryParse(
            workspaceBytesText,
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var workspaceBytes) &&
        workspaceBytes is >= 0 and <= 64L * 1024L * 1024L)
    {
        await using (var generated = new FileStream(
                         Path.Combine(Environment.CurrentDirectory, "outside-artifacts.bin"),
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.ReadWrite))
        {
            generated.SetLength(workspaceBytes);
        }

        // This process deliberately never reaches turn.completed. The gateway's live
        // artifact monitor must terminate it rather than relying on commit validation.
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    if (prompt.Contains("[scenario:hang]", StringComparison.Ordinal))
    {
        await Task.Delay(Timeout.InfiniteTimeSpan);
    }

    if (prompt.Contains("[scenario:stderr-flood]", StringComparison.Ordinal))
    {
        var chunk = new string('x', 8 * 1024);
        for (var index = 0; index < 256; index++)
        {
            await Console.Error.WriteAsync(chunk);
        }

        await Console.Error.FlushAsync();
    }

    if (prompt.Contains("[scenario:fail]", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("FAKE_SECRET: failure detail that must never reach the API");
        Environment.ExitCode = 7;
        return;
    }

    if (prompt.Contains("[scenario:unavailable]", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Authentication required by fake Codex.");
        Environment.ExitCode = 8;
        return;
    }

    string response;
    var artifacts = Path.Combine(Environment.CurrentDirectory, "artifacts");
    if (prompt.Contains("[scenario:write-artifact]", StringComparison.Ordinal))
    {
        Directory.CreateDirectory(artifacts);
        await File.WriteAllTextAsync(Path.Combine(artifacts, "shared-note.txt"), "persisted artifact");
        response = "artifact written";
    }
    else if (prompt.Contains("[scenario:read-artifact]", StringComparison.Ordinal))
    {
        var artifact = Path.Combine(artifacts, "shared-note.txt");
        response = File.Exists(artifact) ? await File.ReadAllTextAsync(artifact) : "artifact missing";
    }
    else if (prompt.Contains("[scenario:list-files]", StringComparison.Ordinal))
    {
        var artifactNames = Directory.Exists(artifacts)
            ? Directory.EnumerateFiles(artifacts).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToArray()
            : [];
        response = string.Join(',', artifactNames);
    }
    else
    {
        response = "Fake Codex response";
    }

    if (ReadOption(codexArguments, "--output-schema") is not null)
    {
        response = prompt.Contains("[scenario:invalid-structured-output]", StringComparison.Ordinal)
            ? "This is not JSON."
            : "{\"result\":\"ok\"}";
    }

    var hasMultipleMessages = prompt.Contains("[scenario:multi-message]", StringComparison.Ordinal);
    if (hasMultipleMessages)
    {
        response = "Final response";
    }

    var outputPath = ReadOption(codexArguments, "--output-last-message");
    if (outputPath is not null)
    {
        await File.WriteAllTextAsync(outputPath, response);
    }

    Console.WriteLine(JsonSerializer.Serialize(new { type = "thread.started", thread_id = "fake-thread" }));
    if (hasMultipleMessages)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            type = "item.completed",
            item = new { id = "item-1", type = "agent_message", text = "Intermediate response" }
        }));
    }

    Console.WriteLine(JsonSerializer.Serialize(new
    {
        type = "item.completed",
        item = new { id = "item-2", type = "agent_message", text = response }
    }));
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        type = "turn.completed",
        usage = new { input_tokens = 10, cached_input_tokens = 0, output_tokens = 3, reasoning_output_tokens = 0 }
    }));
}

async Task RunAppServerAsync(IReadOnlySet<string> configuredMcpServers)
{
    await RecordInvocationAsync("app-server", string.Empty);
    var initialized = false;
    while (await Console.In.ReadLineAsync() is { } line)
    {
        using var document = JsonDocument.Parse(line);
        var root = document.RootElement;
        var method = root.TryGetProperty("method", out var methodElement) ? methodElement.GetString() : null;
        if (method is null)
        {
            continue;
        }

        await RecordAppServerEventAsync(
            method,
            root.TryGetProperty("id", out _),
            root.TryGetProperty("params", out var parameters) ? parameters.Clone() : null);
        if (!root.TryGetProperty("id", out var id))
        {
            if (method == "initialized")
            {
                initialized = true;
            }

            continue;
        }

        if (method != "initialize" && !initialized)
        {
            await WriteAppServerErrorAsync(id.GetInt64(), "Not initialized");
            continue;
        }

        if (method == "initialize" && TryClaimScenario("fail-app-server-initialize-once"))
        {
            await WriteAppServerErrorAsync(id.GetInt64(), "Fake initialize failure");
            continue;
        }

        if (method == "initialize" && TryClaimScenario("hang-app-server-initialize-once"))
        {
            await WaitForGateReleaseAsync("app-server-initialize");
        }

        if (method == "account/login/start" && File.Exists(Path.Combine(scenarioRoot, "hold-device-login-start-response")))
        {
            await WaitForGateReleaseAsync("device-login-start");
        }

        if (method == "account/login/cancel" && File.Exists(Path.Combine(scenarioRoot, "hold-device-login-cancel-response")))
        {
            await WaitForGateReleaseAsync("device-login-cancel");
        }

        object result = method switch
        {
            "initialize" => new { userAgent = "fake-codex" },
            "model/list" => new
            {
                data = new object[]
                {
                    new
                    {
                        id = "internal-sol",
                        model = "gpt-test-sol",
                        displayName = "GPT Test Sol",
                        defaultReasoningEffort = "medium",
                        supportedReasoningEfforts = new[]
                        {
                            new { reasoningEffort = "low" },
                            new { reasoningEffort = "medium" },
                            new { reasoningEffort = "high" }
                        }
                    },
                    new
                    {
                        id = "internal-terra",
                        model = "gpt-test-terra",
                        displayName = "GPT Test Terra",
                        defaultReasoningEffort = "high",
                        supportedReasoningEfforts = new[] { new { reasoningEffort = "high" } }
                    }
                },
                nextCursor = (string?)null
            },
            "account/read" => new { account = new { type = "chatgpt", email = "fake@example.test" } },
            "mcpServerStatus/list" => ListMcpServerStatus(root, configuredMcpServers),
            "account/login/start" => new
            {
                type = "chatgptDeviceCode",
                loginId = "fake-login",
                verificationUrl = "https://example.test/device",
                userCode = "TEST-CODE"
            },
            "account/login/cancel" or "account/logout" => new { },
            _ => new { }
        };
        Console.WriteLine(JsonSerializer.Serialize(new { id = id.GetInt64(), result }));
        await Console.Out.FlushAsync();
        if (method == "account/login/start" && !File.Exists(Path.Combine(scenarioRoot, "hold-device-login")))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                method = "account/login/completed",
                @params = new { loginId = "fake-login", success = true, error = (string?)null }
            }));
            await Console.Out.FlushAsync();
        }
    }
}

object ListMcpServerStatus(JsonElement request, IReadOnlySet<string> configuredMcpServers)
{
    if (!request.TryGetProperty("params", out var parameters) ||
        !parameters.TryGetProperty("detail", out var detail) ||
        detail.GetString() != "toolsAndAuthOnly")
    {
        throw new InvalidOperationException(
            "Fake MCP discovery requires detail=toolsAndAuthOnly.");
    }

    var cursor = parameters.TryGetProperty("cursor", out var cursorElement) &&
                 cursorElement.ValueKind == JsonValueKind.String
        ? cursorElement.GetString()
        : null;
    if (cursor is null &&
        !configuredMcpServers.Contains("alpha") &&
        configuredMcpServers.Contains("zeta"))
    {
        cursor = "tools-page-2";
    }

    if (cursor is null)
    {
        return new
        {
            data = configuredMcpServers.Contains("alpha")
                ? new object[]
            {
                new
                {
                    name = "alpha",
                    serverInfo = new
                    {
                        name = "alpha-runtime",
                        title = "Alpha Runtime",
                        version = "1.2.3",
                        description = "Alpha server description",
                        websiteUrl = "https://metadata.example.test/alpha",
                        icons = new object[]
                        {
                            new
                            {
                                src = "https://metadata.example.test/alpha.svg",
                                mimeType = "image/svg+xml",
                                sizes = new[] { "any" },
                                theme = "light",
                                extension = new { preserved = true }
                            }
                        }
                    },
                    tools = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["write"] = ParseJson(
                            """
                            {
                              "name": "write",
                              "title": "Write record",
                              "description": "Writes one record.",
                              "inputSchema": {
                                "$schema": "https://json-schema.org/draft/2020-12/schema",
                                "type": "object",
                                "properties": {
                                  "value": { "type": "string", "minLength": 1 },
                                  "options": {
                                    "type": "object",
                                    "additionalProperties": { "type": "boolean" }
                                  }
                                },
                                "required": ["value"],
                                "additionalProperties": false
                              },
                              "outputSchema": {
                                "type": "object",
                                "properties": { "record_id": { "type": "string" } },
                                "required": ["record_id"]
                              },
                              "annotations": {
                                "readOnlyHint": false,
                                "destructiveHint": true,
                                "extension": { "risk": 7 }
                              },
                              "icons": [
                                {
                                  "src": "data:image/svg+xml;base64,PHN2Zy8+",
                                  "mimeType": "image/svg+xml",
                                  "extension": ["preserve", 42]
                                }
                              ],
                              "_meta": {
                                "vendor.example/render": { "template": "record-card", "enabled": true },
                                "arbitrary": [1, null, { "nested": "value" }]
                              }
                            }
                            """),
                        ["read"] = ParseJson(
                            """
                            {
                              "name": "read",
                              "inputSchema": {
                                "type": "object",
                                "properties": { "id": { "type": ["string", "integer"] } }
                              }
                            }
                            """),
                        ["delete"] = ParseJson(
                            """
                            {
                              "name": "delete",
                              "title": "Delete record",
                              "description": "Deletes one record.",
                              "inputSchema": {
                                "type": "object",
                                "properties": { "id": { "type": "string" } },
                                "required": ["id"]
                              }
                            }
                            """),
                        ["ungranted"] = ParseJson(
                            """
                            {
                              "name": "ungranted",
                              "description": "Must never cross the per-key grant filter.",
                              "inputSchema": { "type": "object" }
                            }
                            """)
                    },
                    resources = Array.Empty<object>(),
                    resourceTemplates = Array.Empty<object>(),
                    authStatus = "bearerToken"
                }
            }
                : Array.Empty<object>(),
            nextCursor = configuredMcpServers.Contains("zeta") ? "tools-page-2" : null
        };
    }

    if (cursor == "tools-page-2")
    {
        return new
        {
            data = configuredMcpServers.Contains("zeta")
                ? new object[]
            {
                new
                {
                    name = "zeta",
                    serverInfo = new
                    {
                        name = "zeta-runtime",
                        title = (string?)null,
                        version = "9.8.7",
                        description = (string?)null,
                        websiteUrl = (string?)null,
                        icons = (object[]?)null
                    },
                    tools = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
                    {
                        ["lookup"] = ParseJson(
                            """
                            {
                              "name": "lookup",
                              "description": "Looks up a value.",
                              "inputSchema": {
                                "oneOf": [
                                  { "type": "string" },
                                  { "type": "object", "additionalProperties": true }
                                ]
                              },
                              "annotations": { "readOnlyHint": true }
                            }
                            """),
                        ["mutate"] = ParseJson(
                            """
                            {
                              "name": "mutate",
                              "inputSchema": { "type": "object" }
                            }
                            """)
                    },
                    resources = Array.Empty<object>(),
                    resourceTemplates = Array.Empty<object>(),
                    authStatus = "unsupported"
                }
            }
                : Array.Empty<object>(),
            nextCursor = (string?)null
        };
    }

    throw new InvalidOperationException($"Unexpected fake MCP discovery cursor '{cursor}'.");
}

static JsonElement ParseJson(string value) => JsonSerializer.Deserialize<JsonElement>(value);

static IReadOnlySet<string> ReadConfiguredMcpServers(IEnumerable<string> codexArguments)
{
    const string prefix = "mcp_servers.";
    var result = new HashSet<string>(StringComparer.Ordinal);
    foreach (var argument in codexArguments)
    {
        if (!argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            continue;
        }

        var remainder = argument[prefix.Length..];
        var end = remainder.IndexOfAny(['.', '=']);
        var name = end < 0 ? remainder : remainder[..end];
        if (name.Length > 0)
        {
            result.Add(name);
        }
    }

    return result;
}

async Task RecordAppServerEventAsync(string method, bool hasId, JsonElement? parameters)
{
    var directory = Path.Combine(scenarioRoot, "app-server-events");
    Directory.CreateDirectory(directory);
    var record = new
    {
        processId = Environment.ProcessId,
        method,
        hasId,
        parameters
    };
    var path = Path.Combine(directory, DateTime.UtcNow.Ticks + "-" + Guid.NewGuid().ToString("N") + ".json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record));
}

async Task WriteAppServerErrorAsync(long id, string message)
{
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        id,
        error = new { code = -32000, message }
    }));
    await Console.Out.FlushAsync();
}

bool TryClaimScenario(string name)
{
    if (!File.Exists(Path.Combine(scenarioRoot, name)))
    {
        return false;
    }

    var claimPath = Path.Combine(scenarioRoot, name + ".claimed");
    try
    {
        using var claim = new FileStream(claimPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(claim);
        writer.Write(Environment.ProcessId);
        return true;
    }
    catch (IOException)
    {
        return false;
    }
}

async Task RecordInvocationAsync(
    string invocationMode,
    string stdin,
    IReadOnlyList<string>? invocationArguments = null)
{
    var directory = Path.Combine(scenarioRoot, "invocations");
    Directory.CreateDirectory(directory);
    var record = new
    {
        mode = invocationMode,
        arguments = invocationArguments ?? arguments,
        workingDirectory = Environment.CurrentDirectory,
        stdin,
        codexHome = Environment.GetEnvironmentVariable("CODEX_HOME"),
        processId = Environment.ProcessId
    };
    var path = Path.Combine(directory, DateTime.UtcNow.Ticks + "-" + Guid.NewGuid().ToString("N") + ".json");
    await File.WriteAllTextAsync(path, JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));
}

async Task<string> RegisterFakeContainerAsync(string containerName)
{
    var directory = Path.Combine(scenarioRoot, "containers");
    Directory.CreateDirectory(directory);
    var path = FakeContainerStatePath(directory, containerName);
    var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(new
    {
        name = containerName,
        processId = Environment.ProcessId
    }));
    File.Move(temporaryPath, path, true);
    return path;
}

async Task ListFakeContainersAsync()
{
    var directory = Path.Combine(scenarioRoot, "containers");
    if (!Directory.Exists(directory))
    {
        return;
    }

    foreach (var statePath in Directory.EnumerateFiles(directory, "*.json"))
    {
        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = await JsonDocument.ParseAsync(stream);
            Console.WriteLine(document.RootElement.GetProperty("name").GetString());
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }
    }
}

async Task RemoveFakeContainersAsync(IReadOnlyList<string> engineArguments)
{
    var directory = Path.Combine(scenarioRoot, "containers");
    foreach (var containerName in engineArguments.Skip(1).Where(value => !value.StartsWith('-')))
    {
        var statePath = FakeContainerStatePath(directory, containerName);
        try
        {
            await using var stream = new FileStream(
                statePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var document = await JsonDocument.ParseAsync(stream);
            var processId = document.RootElement.GetProperty("processId").GetInt32();
            if (processId != Environment.ProcessId)
            {
                using var process = Process.GetProcessById(processId);
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(5000);
                }
            }
        }
        catch (FileNotFoundException)
        {
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            TryDelete(statePath);
        }
    }
}

static string FakeContainerStatePath(string directory, string containerName)
{
    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(containerName)));
    return Path.Combine(directory, hash + ".json");
}

static void TryDelete(string path)
{
    try
    {
        File.Delete(path);
    }
    catch (IOException)
    {
    }
    catch (UnauthorizedAccessException)
    {
    }
}

async Task WaitForGateReleaseAsync(string gateName)
{
    if (gateName.Length is 0 or > 64 || gateName.Any(character =>
            !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
    {
        throw new InvalidOperationException("Fake Codex gate names must be filesystem-safe.");
    }

    var directory = Path.Combine(scenarioRoot, "gates");
    Directory.CreateDirectory(directory);
    var startedPath = Path.Combine(directory, gateName + ".started.json");
    var temporaryPath = startedPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
    await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(new
    {
        processId = Environment.ProcessId,
        gateName,
        workingDirectory = Environment.CurrentDirectory
    }));
    File.Move(temporaryPath, startedPath);

    var releasePath = Path.Combine(directory, gateName + ".release");
    while (!File.Exists(releasePath))
    {
        await Task.Delay(10);
    }
}

static ContainerRun ParseContainerRun(IReadOnlyList<string> engineArguments)
{
    var mounts = new List<ContainerMount>();
    var environment = new Dictionary<string, string>(StringComparer.Ordinal);
    var workingDirectory = "/workspace";
    var index = 1;

    while (index < engineArguments.Count)
    {
        var value = engineArguments[index];
        if (value.Length == 0 || value[0] != '-')
        {
            // The first positional value is the image. Everything after it is the
            // configured entrypoint's argv (the entrypoint itself is not repeated).
            return new ContainerRun(
                mounts,
                environment,
                workingDirectory,
                engineArguments.Skip(index + 1).ToArray());
        }

        if (TryReadInlineOption(value, "--mount", out var inlineMount))
        {
            mounts.Add(ParseMount(inlineMount));
            index++;
            continue;
        }

        if (TryReadInlineOption(value, "--volume", out var inlineVolume))
        {
            mounts.Add(ParseVolume(inlineVolume));
            index++;
            continue;
        }

        if (TryReadInlineOption(value, "--env", out var inlineEnvironment))
        {
            AddEnvironment(environment, inlineEnvironment);
            index++;
            continue;
        }

        if (TryReadInlineOption(value, "--workdir", out var inlineWorkingDirectory))
        {
            workingDirectory = inlineWorkingDirectory;
            index++;
            continue;
        }

        if (value is "--mount")
        {
            mounts.Add(ParseMount(ReadRequiredValue(engineArguments, ref index, value)));
            continue;
        }

        if (value is "--volume" or "-v")
        {
            mounts.Add(ParseVolume(ReadRequiredValue(engineArguments, ref index, value)));
            continue;
        }

        if (value is "--env" or "-e")
        {
            AddEnvironment(environment, ReadRequiredValue(engineArguments, ref index, value));
            continue;
        }

        if (value is "--workdir" or "-w")
        {
            workingDirectory = ReadRequiredValue(engineArguments, ref index, value);
            continue;
        }

        if (ContainerOptionTakesValue(value))
        {
            _ = ReadRequiredValue(engineArguments, ref index, value);
            continue;
        }

        // All container flags currently emitted by the gateway that do not take a
        // value (for example --interactive and --read-only) are safely skipped here.
        index++;
    }

    throw new InvalidOperationException("Fake container run did not include an image.");
}

static bool ContainerOptionTakesValue(string value) => value is
    "--name" or
    "--label" or
    "--network" or
    "--memory" or
    "--memory-swap" or
    "--cpus" or
    "--pids-limit" or
    "--tmpfs" or
    "--entrypoint" or
    "--security-opt" or
    "--cap-drop" or
    "--user" or
    "-u" or
    "--hostname" or
    "--stop-timeout" or
    "--pull";

static string ReadRequiredValue(IReadOnlyList<string> values, ref int index, string option)
{
    if (index + 1 >= values.Count)
    {
        throw new InvalidOperationException($"Fake container option '{option}' requires a value.");
    }

    index += 2;
    return values[index - 1];
}

static bool TryReadInlineOption(string value, string option, out string optionValue)
{
    var prefix = option + "=";
    if (value.StartsWith(prefix, StringComparison.Ordinal))
    {
        optionValue = value[prefix.Length..];
        return true;
    }

    optionValue = string.Empty;
    return false;
}

static ContainerMount ParseMount(string value)
{
    var fields = value.Split(',')
        .Select(part => part.Split('=', 2))
        .Where(part => part.Length == 2)
        .ToDictionary(part => part[0], part => part[1], StringComparer.OrdinalIgnoreCase);
    var source = ReadField(fields, "src", "source");
    var target = ReadField(fields, "dst", "destination", "target");
    var subpath = fields.GetValueOrDefault("volume-subpath");
    if (!string.IsNullOrEmpty(subpath) && Path.IsPathFullyQualified(source))
    {
        source = Path.Combine(source, subpath.Replace('/', Path.DirectorySeparatorChar));
    }

    return new ContainerMount(source, NormalizeContainerPath(target));
}

static ContainerMount ParseVolume(string value)
{
    var separator = value.LastIndexOf(':');
    if (separator <= 1 || separator == value.Length - 1)
    {
        throw new InvalidOperationException("Fake container volume must contain source and target paths.");
    }

    // Preserve a Windows drive prefix by splitting at the final colon.
    var source = value[..separator];
    var target = value[(separator + 1)..];
    var modeSeparator = target.IndexOf(':');
    if (modeSeparator >= 0)
    {
        target = target[..modeSeparator];
    }

    return new ContainerMount(source, NormalizeContainerPath(target));
}

static string ReadField(IReadOnlyDictionary<string, string> fields, params string[] names)
{
    foreach (var name in names)
    {
        if (fields.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    throw new InvalidOperationException("Fake container mount is missing a source or target.");
}

static void AddEnvironment(IDictionary<string, string> environment, string assignment)
{
    var separator = assignment.IndexOf('=');
    if (separator <= 0)
    {
        environment[assignment] = Environment.GetEnvironmentVariable(assignment) ?? string.Empty;
        return;
    }

    environment[assignment[..separator]] = assignment[(separator + 1)..];
}

static string? ResolveContainerPath(string containerPath, IReadOnlyList<ContainerMount> mounts)
{
    var normalized = NormalizeContainerPath(containerPath);
    foreach (var mount in mounts.OrderByDescending(candidate => candidate.Target.Length))
    {
        if (string.Equals(normalized, mount.Target, StringComparison.Ordinal))
        {
            return mount.Source;
        }

        var prefix = mount.Target.EndsWith('/') ? mount.Target : mount.Target + "/";
        if (normalized.StartsWith(prefix, StringComparison.Ordinal))
        {
            var relative = normalized[prefix.Length..].Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(mount.Source, relative);
        }
    }

    return null;
}

static string ResolveEnvironmentValue(string value, IReadOnlyList<ContainerMount> mounts) =>
    ResolveContainerPath(value, mounts) ?? value;

static string NormalizeContainerPath(string value)
{
    var normalized = value.Replace('\\', '/').TrimEnd('/');
    return normalized.Length == 0 ? "/" : normalized;
}

static string? ReadScenarioValue(string prompt, string scenario)
{
    var prefix = "[scenario:" + scenario + ":";
    var start = prompt.IndexOf(prefix, StringComparison.Ordinal);
    if (start < 0)
    {
        return null;
    }

    start += prefix.Length;
    var end = prompt.IndexOf(']', start);
    return end < 0 ? null : prompt[start..end];
}

static string? ReadOption(IReadOnlyList<string> values, string name)
{
    var index = values.IndexOf(name);
    return index >= 0 && index + 1 < values.Count ? values[index + 1] : null;
}

public sealed class FakeCodexMarker;

internal sealed record ContainerRun(
    IReadOnlyList<ContainerMount> Mounts,
    IReadOnlyDictionary<string, string> Environment,
    string WorkingDirectory,
    IReadOnlyList<string> CommandArguments);

internal sealed record ContainerMount(string Source, string Target);

internal static class ListExtensions
{
    public static int IndexOf(this IReadOnlyList<string> values, string value)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (string.Equals(values[index], value, StringComparison.Ordinal))
            {
                return index;
            }
        }

        return -1;
    }
}
