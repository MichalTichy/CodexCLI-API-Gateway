using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex.AppServer;

public sealed class CodexAppServerClient : ICodexControlPlane, IDisposable, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly CodexOptions _options;
    private readonly string _codexHome;
    private readonly ILogger<CodexAppServerClient> _logger;
    private readonly RunCoordinator _runs;
    private readonly SemaphoreSlim _startGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _loginOperationGate = new(1, 1);
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly ConcurrentDictionary<string, (bool Success, string? Error)> _earlyLoginCompletions = new(StringComparer.Ordinal);
    private readonly object _loginGate = new();
    private Process? _process;
    private Task? _readerTask;
    private bool _processReady;
    private long _nextRequestId;
    private DeviceLogin? _deviceLogin;
    private bool _loginStartInProgress;
    private long? _loginGeneration;
    private CancellationTokenSource? _loginTimeout;
    private IReadOnlyList<CodexModel>? _cachedModels;
    private DateTimeOffset _modelCacheExpires;
    private int _disposed;

    public CodexAppServerClient(
        IOptions<CodexOptions> options,
        IHostEnvironment environment,
        ILogger<CodexAppServerClient> logger,
        RunCoordinator runs)
    {
        _options = options.Value;
        _logger = logger;
        _runs = runs;
        _codexHome = Path.GetFullPath(Path.IsPathRooted(_options.HomePath)
            ? _options.HomePath
            : Path.Combine(environment.ContentRootPath, _options.HomePath));
    }

    public async Task<IReadOnlyList<CodexModel>> GetModelsAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _cachedModels is not null && _modelCacheExpires > DateTimeOffset.UtcNow)
        {
            return _cachedModels;
        }

        var models = new List<CodexModel>();
        string? cursor = null;
        do
        {
            var result = await SendAsync("model/list", new { limit = 100, cursor, includeHidden = false }, cancellationToken);
            models.AddRange(ParseModels(result));
            cursor = GetString(result, "nextCursor");
        }
        while (!string.IsNullOrWhiteSpace(cursor));

        if (models.Count == 0)
        {
            throw new CodexUnavailableException("Codex did not report any available models.");
        }

        _cachedModels = models;
        _modelCacheExpires = DateTimeOffset.UtcNow.AddSeconds(_options.ModelCacheSeconds);
        return models;
    }

    public async Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken)
    {
        var result = await SendAsync("account/read", new { refreshToken = false }, cancellationToken);
        if (!result.TryGetProperty("account", out var account) || account.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return new CodexAccountStatus(false, null, null);
        }

        return new CodexAccountStatus(
            true,
            GetString(account, "type") ?? GetString(account, "accountType"),
            GetString(account, "email"));
    }

    public async Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken)
    {
        await _loginOperationGate.WaitAsync(cancellationToken);
        long? generation = null;
        try
        {
            lock (_loginGate)
            {
                if (_loginStartInProgress || _deviceLogin is { Status: DeviceLoginStatus.Pending })
                {
                    throw GatewayException.InvalidRequest("A Codex device login is already in progress.", "login_in_progress");
                }

                _loginStartInProgress = true;
            }

            generation = _runs.BeginAuthenticationChange();
            BeginLoginTimeout(generation.Value);
            // Once the shared-auth transition begins it must reach a terminal state even if the
            // initiating HTTP request disconnects. SendCoreAsync still applies its own timeout.
            var result = await SendAsync("account/login/start", new { type = "chatgptDeviceCode" }, CancellationToken.None);
            var login = new DeviceLogin(
                GetString(result, "loginId") ?? throw new CodexUnavailableException("Codex returned an invalid device-login response."),
                GetString(result, "verificationUrl") ?? GetString(result, "verificationUri") ?? "https://auth.openai.com/codex/device",
                GetString(result, "userCode") ?? throw new CodexUnavailableException("Codex returned an invalid device-login response."),
                DeviceLoginStatus.Pending);
            if (_earlyLoginCompletions.TryRemove(login.LoginId, out var completion))
            {
                login = login with
                {
                    Status = completion.Success ? DeviceLoginStatus.Completed : DeviceLoginStatus.Failed,
                    Error = completion.Success ? null : completion.Error ?? "Codex device login failed."
                };
            }

            lock (_loginGate)
            {
                _deviceLogin = login;
                _loginStartInProgress = false;
                if (login.Status != DeviceLoginStatus.Pending)
                {
                    _loginGeneration = null;
                }
            }

            if (login.Status != DeviceLoginStatus.Pending)
            {
                CancelLoginTimeout();
                _runs.EndAuthenticationChange(generation.Value);
            }

            return login;
        }
        catch
        {
            if (generation is not null)
            {
                // A timed-out/rejected start may still have reached App Server. Stop that exact
                // control-plane process so it cannot complete a now-untracked login later.
                await InvalidateAppServerAsync();
            }

            lock (_loginGate)
            {
                _loginStartInProgress = false;
                if (generation is not null && _loginGeneration == generation)
                {
                    _loginGeneration = null;
                }
            }

            CancelLoginTimeout();
            if (generation is not null)
            {
                _runs.EndAuthenticationChange(generation.Value);
            }

            throw;
        }
        finally
        {
            _loginOperationGate.Release();
        }
    }

    public Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_loginGate)
        {
            return Task.FromResult(_deviceLogin);
        }
    }

    public async Task CancelDeviceLoginAsync(CancellationToken cancellationToken)
    {
        await _loginOperationGate.WaitAsync(cancellationToken);
        try
        {
            DeviceLogin? login;
            long? generation;
            lock (_loginGate)
            {
                login = _deviceLogin;
                generation = _loginGeneration;
            }

            if (login is not { Status: DeviceLoginStatus.Pending })
            {
                return;
            }

            try
            {
                await SendAsync("account/login/cancel", new { loginId = login.LoginId }, CancellationToken.None);
            }
            catch
            {
                await InvalidateAppServerAsync();
                CompleteFailedLogin(
                    login.LoginId,
                    generation,
                    "Codex device login cancellation could not be confirmed.");
                throw;
            }

            var cancelled = false;
            lock (_loginGate)
            {
                if (_deviceLogin is { Status: DeviceLoginStatus.Pending } current &&
                    current.LoginId == login.LoginId &&
                    _loginGeneration == generation)
                {
                    _deviceLogin = current with { Status = DeviceLoginStatus.Cancelled };
                    _loginGeneration = null;
                    cancelled = true;
                }
            }

            if (cancelled && generation is not null)
            {
                CancelLoginTimeout();
                _runs.EndAuthenticationChange(generation.Value);
            }
        }
        finally
        {
            _loginOperationGate.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _loginOperationGate.WaitAsync(cancellationToken);
        long? generation = null;
        try
        {
            lock (_loginGate)
            {
                if (_loginStartInProgress || _deviceLogin is { Status: DeviceLoginStatus.Pending })
                {
                    throw GatewayException.InvalidRequest("Cancel the pending Codex device login before logging out.", "login_in_progress");
                }
            }

            generation = _runs.BeginAuthenticationChange();
            try
            {
                await SendAsync("account/logout", new { }, CancellationToken.None);
            }
            catch
            {
                await InvalidateAppServerAsync();
                throw;
            }

            _cachedModels = null;
            lock (_loginGate)
            {
                _deviceLogin = null;
            }
        }
        finally
        {
            if (generation is not null)
            {
                _runs.EndAuthenticationChange(generation.Value);
            }

            _loginOperationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        if (_readerTask is not null)
        {
            try
            {
                await _readerTask;
            }
            catch
            {
            }
        }

    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        if (_process is { } process)
        {
            Volatile.Write(ref _processReady, false);
            try
            {
                if (!process.HasExited)
                {
                    process.StandardInput.Close();
                    if (!process.WaitForExit(2000))
                    {
                        process.Kill(true);
                        process.WaitForExit(5000);
                    }
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        _startGate.Dispose();
        _writeGate.Dispose();
        _loginOperationGate.Dispose();
        _loginTimeout?.Cancel();
        _loginTimeout?.Dispose();
    }

    private async Task<JsonElement> SendAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        await EnsureStartedAsync(cancellationToken);
        return await SendCoreAsync(method, parameters, cancellationToken);
    }

    private async Task EnsureStartedAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _processReady) &&
            _readerTask is { IsCompleted: false } &&
            _process is { HasExited: false })
        {
            return;
        }

        await _startGate.WaitAsync(cancellationToken);
        try
        {
            if (Volatile.Read(ref _processReady) &&
                _readerTask is { IsCompleted: false } &&
                _process is { HasExited: false })
            {
                return;
            }

            if (_process is { } staleProcess)
            {
                var staleReader = _readerTask;
                _process = null;
                _readerTask = null;
                Volatile.Write(ref _processReady, false);
                StopProcess(staleProcess);
                if (staleReader is not null)
                {
                    await ObserveReaderAsync(staleReader);
                }

                staleProcess.Dispose();
                FailPendingLogin("The Codex App Server stopped before device login completed.");
            }

            Directory.CreateDirectory(_codexHome);
            var temporaryDirectory = Path.Combine(_codexHome, ".tmp");
            Directory.CreateDirectory(temporaryDirectory);
            Directory.CreateDirectory(Path.Combine(_codexHome, ".cache"));
            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                WorkingDirectory = _codexHome,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            CodexProcessEnvironment.Configure(startInfo, _codexHome, temporaryDirectory, []);
            foreach (var prefix in _options.ArgumentPrefix)
            {
                startInfo.ArgumentList.Add(prefix);
            }

            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");
            startInfo.ArgumentList.Add("--config");
            startInfo.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            try
            {
                if (!process.Start())
                {
                    throw new InvalidOperationException("Process did not start.");
                }
            }
            catch (Exception)
            {
                process.Dispose();
                throw new CodexUnavailableException("The Codex App Server could not be started.");
            }

            _process = process;
            Volatile.Write(ref _processReady, false);
            var readerTask = Task.Run(() => ReadLoopAsync(process));
            _readerTask = readerTask;
            _ = Task.Run(() => DrainErrorsAsync(process));
            try
            {
                await SendCoreAsync("initialize", new
                {
                    clientInfo = new
                    {
                        name = "codex-cli-api-gateway",
                        title = "Codex CLI API Gateway",
                        version = typeof(CodexAppServerClient).Assembly.GetName().Version?.ToString() ?? "0.1.0"
                    }
                }, cancellationToken);
                await WriteNotificationAsync("initialized", new { }, cancellationToken);
                Volatile.Write(ref _processReady, true);
            }
            catch
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                    _readerTask = null;
                }

                Volatile.Write(ref _processReady, false);

                StopProcess(process);
                await ObserveReaderAsync(readerTask);
                process.Dispose();
                throw;
            }
        }
        finally
        {
            _startGate.Release();
        }
    }

    private async Task<JsonElement> SendCoreAsync(string method, object parameters, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new CodexUnavailableException("The Codex App Server is unavailable.");
        }

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
        {
            throw new InvalidOperationException("Duplicate App Server request ID.");
        }

        try
        {
            await WriteMessageAsync(new { method, id, @params = parameters }, cancellationToken);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(_options.AppServerRequestTimeoutSeconds));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                return await completion.Task.WaitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new CodexUnavailableException("The Codex App Server did not respond in time.");
            }
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task WriteNotificationAsync(string method, object parameters, CancellationToken cancellationToken) =>
        WriteMessageAsync(new { method, @params = parameters }, cancellationToken);

    private async Task WriteMessageAsync(object message, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new CodexUnavailableException("The Codex App Server is unavailable.");
        }

        var json = JsonSerializer.Serialize(message, JsonOptions);
        await _writeGate.WaitAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            throw new CodexUnavailableException("The Codex App Server connection was closed.");
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                HandleMessage(line);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Codex App Server output loop stopped.");
        }
        finally
        {
            var ownsConnection = ReferenceEquals(_process, process);
            if (ownsConnection)
            {
                Volatile.Write(ref _processReady, false);
            }

            var exception = new CodexUnavailableException("The Codex App Server stopped unexpectedly.");
            foreach (var pending in _pending.Values)
            {
                pending.TrySetException(exception);
            }

            if (ownsConnection)
            {
                FailPendingLogin("The Codex App Server stopped before device login completed.");
            }
        }
    }

    private void HandleMessage(string line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var id) && _pending.TryGetValue(id, out var completion))
            {
                if (root.TryGetProperty("error", out _))
                {
                    completion.TrySetException(new CodexUnavailableException("The Codex App Server rejected the request."));
                }
                else if (root.TryGetProperty("result", out var result))
                {
                    completion.TrySetResult(result.Clone());
                }

                return;
            }

            if (!root.TryGetProperty("method", out var methodElement))
            {
                return;
            }

            var method = methodElement.GetString();
            if (method == "account/login/completed" && root.TryGetProperty("params", out var parameters))
            {
                var loginId = GetString(parameters, "loginId");
                var success = parameters.TryGetProperty("success", out var succeeded) && succeeded.ValueKind == JsonValueKind.True;
                long? generation = null;
                lock (_loginGate)
                {
                    if (_deviceLogin is { Status: DeviceLoginStatus.Pending } && (loginId is null || loginId == _deviceLogin.LoginId))
                    {
                        _deviceLogin = _deviceLogin with
                        {
                            Status = success ? DeviceLoginStatus.Completed : DeviceLoginStatus.Failed,
                            Error = success ? null : "Codex device login failed."
                        };
                        generation = _loginGeneration;
                        _loginGeneration = null;
                    }
                    else if (_loginStartInProgress && loginId is not null)
                    {
                        _earlyLoginCompletions[loginId] = (success, GetString(parameters, "error"));
                        generation = _loginGeneration;
                        _loginGeneration = null;
                    }
                }

                if (success)
                {
                    _cachedModels = null;
                }

                if (generation is not null)
                {
                    CancelLoginTimeout();
                    _runs.EndAuthenticationChange(generation.Value);
                }
            }
        }
    }

    private void BeginLoginTimeout(long generation)
    {
        var timeout = new CancellationTokenSource();
        lock (_loginGate)
        {
            _loginTimeout?.Cancel();
            _loginTimeout?.Dispose();
            _loginGeneration = generation;
            _loginTimeout = timeout;
        }

        _ = ExpireLoginAsync(generation, timeout);
    }

    private async Task ExpireLoginAsync(long generation, CancellationTokenSource timeout)
    {
        try
        {
            var duration = TimeSpan.FromSeconds(_options.DeviceLoginTimeoutSeconds);
            await Task.Delay(duration, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return;
        }

        await _loginOperationGate.WaitAsync(CancellationToken.None);
        try
        {
            DeviceLogin? login;
            lock (_loginGate)
            {
                login = _loginGeneration == generation && _deviceLogin is { Status: DeviceLoginStatus.Pending } pending
                    ? pending
                    : null;
            }

            if (login is not null)
            {
                try
                {
                    await SendAsync(
                        "account/login/cancel",
                        new { loginId = login.LoginId },
                        CancellationToken.None);
                }
                catch
                {
                    // Killing the polling App Server is the fail-closed fallback when cancellation
                    // cannot be acknowledged. It prevents a device code from mutating auth later.
                    await InvalidateAppServerAsync();
                }
            }
            else
            {
                // This can only be a start request that never reached a response. Stop App Server
                // before releasing admission so a late response cannot create an untracked login.
                lock (_loginGate)
                {
                    if (_loginGeneration != generation)
                    {
                        return;
                    }
                }

                await InvalidateAppServerAsync();
            }

            CompleteFailedLogin(login?.LoginId, generation, "Codex device login timed out.");
        }
        finally
        {
            _loginOperationGate.Release();
            timeout.Dispose();
        }
    }

    private void CancelLoginTimeout()
    {
        CancellationTokenSource? timeout;
        lock (_loginGate)
        {
            timeout = _loginTimeout;
            _loginTimeout = null;
        }

        timeout?.Cancel();
        timeout?.Dispose();
    }

    private void FailPendingLogin(string message)
    {
        long? generation;
        lock (_loginGate)
        {
            generation = _loginGeneration;
            if (generation is null)
            {
                return;
            }

            if (_deviceLogin is { Status: DeviceLoginStatus.Pending } login)
            {
                _deviceLogin = login with { Status = DeviceLoginStatus.Failed, Error = message };
            }

            _loginStartInProgress = false;
            _loginGeneration = null;
        }

        CancelLoginTimeout();
        _runs.EndAuthenticationChange(generation.Value);
    }

    private void CompleteFailedLogin(string? loginId, long? generation, string message)
    {
        var completed = false;
        lock (_loginGate)
        {
            if (generation is null || _loginGeneration != generation)
            {
                return;
            }

            if (_deviceLogin is { Status: DeviceLoginStatus.Pending } login &&
                (loginId is null || login.LoginId == loginId))
            {
                _deviceLogin = login with { Status = DeviceLoginStatus.Failed, Error = message };
            }

            _loginStartInProgress = false;
            _loginGeneration = null;
            completed = true;
        }

        if (completed)
        {
            CancelLoginTimeout();
            _runs.EndAuthenticationChange(generation.Value);
        }
    }

    private async Task InvalidateAppServerAsync()
    {
        Process? process = null;
        Task? readerTask = null;
        await _startGate.WaitAsync(CancellationToken.None);
        try
        {
            Volatile.Write(ref _processReady, false);
            if (_process is not null)
            {
                process = _process;
                readerTask = _readerTask;
                _process = null;
                _readerTask = null;
                StopProcess(process);
                if (readerTask is not null)
                {
                    await ObserveReaderAsync(readerTask);
                }

                process.Dispose();
            }
        }
        finally
        {
            _startGate.Release();
        }

    }

    private static void StopProcess(Process process)
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

    private static async Task ObserveReaderAsync(Task readerTask)
    {
        try
        {
            await readerTask.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch
        {
        }
    }

    private static IReadOnlyList<CodexModel> ParseModels(JsonElement result)
    {
        if (!result.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var models = new List<CodexModel>();
        foreach (var item in data.EnumerateArray())
        {
            var id = GetString(item, "model") ?? GetString(item, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var efforts = new List<string>();
            if (item.TryGetProperty("supportedReasoningEfforts", out var supported) && supported.ValueKind == JsonValueKind.Array)
            {
                foreach (var effort in supported.EnumerateArray())
                {
                    var value = effort.ValueKind == JsonValueKind.String
                        ? effort.GetString()
                        : GetString(effort, "reasoningEffort") ?? GetString(effort, "effort");
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        efforts.Add(value);
                    }
                }
            }

            var defaultEffort = GetString(item, "defaultReasoningEffort") ?? efforts.FirstOrDefault() ?? "medium";
            if (efforts.Count == 0)
            {
                efforts.Add(defaultEffort);
            }

            models.Add(new CodexModel(
                id,
                GetString(item, "displayName") ?? GetString(item, "name") ?? id,
                efforts.Distinct(StringComparer.Ordinal).ToArray(),
                defaultEffort));
        }

        return models;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static async Task DrainErrorsAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync() is not null)
        {
            // App Server diagnostics stay in the process boundary and are never sent to clients.
        }
    }
}
