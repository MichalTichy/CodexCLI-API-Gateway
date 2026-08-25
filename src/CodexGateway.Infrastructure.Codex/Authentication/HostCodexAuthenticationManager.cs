using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using CodexGateway.Infrastructure.Codex.Containers;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Codex.Authentication;

public sealed class HostCodexAuthenticationManager : ICodexAuthenticationManager, IDisposable
{
    private static readonly Regex VerificationUrlPattern = new(
        "https?://[^\\s<>()]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex DeviceCodePattern = new(
        "\\b[A-Z0-9]{4,8}(?:-[A-Z0-9]{4,8})+\\b",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private static readonly Regex AnsiEscapePattern = new(
        "\\x1B(?:\\[[0-?]*[ -/]*[@-~]|\\][^\\x07\\x1B]*(?:\\x07|\\x1B\\\\))",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly CodexOptions _options;
    private readonly string _codexHome;
    private readonly RunCoordinator _runs;
    private readonly ILogger<HostCodexAuthenticationManager> _logger;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _stateGate = new();
    private Process? _loginProcess;
    private CancellationTokenSource? _loginLifetime;
    private DeviceLogin? _login;
    private long? _authenticationGeneration;
    private int _disposed;

    public HostCodexAuthenticationManager(
        IOptions<CodexOptions> options,
        IHostEnvironment environment,
        RunCoordinator runs,
        ILogger<HostCodexAuthenticationManager> logger)
    {
        _options = options.Value;
        _runs = runs;
        _logger = logger;
        _codexHome = Path.GetFullPath(Path.IsPathRooted(_options.HomePath)
            ? _options.HomePath
            : Path.Combine(environment.ContentRootPath, _options.HomePath));
    }

    public async Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(["login", "status"], cancellationToken);
        var output = result.StandardOutput + Environment.NewLine + result.StandardError;
        if (output.Contains("not logged in", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("not authenticated", StringComparison.OrdinalIgnoreCase))
        {
            return new CodexAccountStatus(false, null, null);
        }

        if (result.ExitCode != 0)
        {
            throw new CodexUnavailableException("Codex login status could not be read.");
        }

        if (output.Contains("logged in", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("authenticated", StringComparison.OrdinalIgnoreCase))
        {
            var accountType = output.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase)
                ? "chatgpt"
                : output.Contains("API key", StringComparison.OrdinalIgnoreCase)
                    ? "api-key"
                    : null;
            return new CodexAccountStatus(true, accountType, null);
        }

        throw new CodexUnavailableException("Codex returned an unrecognized login status.");
    }

    public async Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            lock (_stateGate)
            {
                if (_loginProcess is not null || _login is { Status: DeviceLoginStatus.Pending })
                {
                    throw GatewayException.InvalidRequest(
                        "A Codex device login is already in progress.",
                        "login_in_progress");
                }
            }

            var generation = _runs.BeginAuthenticationChange();
            Process? process = null;
            try
            {
                var startInfo = CreateStartInfo(["login", "--device-auth"]);
                process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                var loginId = Guid.NewGuid().ToString("N");
                var output = new DeviceLoginOutput(loginId);
                var loginReady = new TaskCompletionSource<DeviceLogin>(TaskCreationOptions.RunContinuationsAsynchronously);
                process.OutputDataReceived += (_, eventArguments) =>
                    CaptureLoginOutput(process, output, eventArguments.Data, loginReady);
                process.ErrorDataReceived += (_, eventArguments) =>
                    CaptureLoginOutput(process, output, eventArguments.Data, loginReady);

                if (!process.Start())
                {
                    throw new InvalidOperationException("Process did not start.");
                }

                var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(_options.DeviceLoginTimeoutSeconds));
                lock (_stateGate)
                {
                    _login = null;
                    _loginProcess = process;
                    _loginLifetime = lifetime;
                    _authenticationGeneration = generation;
                }

                // Codex can print the device URL and code immediately. Register the
                // process before reading output so those first lines cannot be discarded.
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                _ = MonitorLoginAsync(process, output, loginReady, lifetime, generation);
                return await loginReady.Task.WaitAsync(cancellationToken);
            }
            catch
            {
                if (process is not null)
                {
                    ContainerRuntime.KillProcessTree(process);
                    process.Dispose();
                }

                lock (_stateGate)
                {
                    if (ReferenceEquals(_loginProcess, process))
                    {
                        _loginProcess = null;
                        _loginLifetime?.Dispose();
                        _loginLifetime = null;
                        _authenticationGeneration = null;
                    }
                }

                _runs.EndAuthenticationChange(generation);
                throw;
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_stateGate)
        {
            return Task.FromResult(_login);
        }
    }

    public async Task CancelDeviceLoginAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        try
        {
            Process? process;
            CancellationTokenSource? lifetime;
            long? generation;
            lock (_stateGate)
            {
                process = _loginProcess;
                lifetime = _loginLifetime;
                generation = _authenticationGeneration;
                if (process is null)
                {
                    return;
                }

                _loginProcess = null;
                _loginLifetime = null;
                _authenticationGeneration = null;
                _login = (_login ?? new DeviceLogin(string.Empty, string.Empty, string.Empty, DeviceLoginStatus.Pending)) with
                {
                    Status = DeviceLoginStatus.Cancelled,
                    Error = null
                };
            }

            lifetime?.Cancel();
            ContainerRuntime.KillProcessTree(process);
            process.Dispose();
            lifetime?.Dispose();
            if (generation is not null)
            {
                _runs.EndAuthenticationChange(generation.Value);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken);
        long? generation = null;
        try
        {
            lock (_stateGate)
            {
                if (_loginProcess is not null || _login is { Status: DeviceLoginStatus.Pending })
                {
                    throw GatewayException.InvalidRequest(
                        "Cancel the pending Codex device login before logging out.",
                        "login_in_progress");
                }
            }

            generation = _runs.BeginAuthenticationChange();
            var result = await ExecuteAsync(["logout"], cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new CodexUnavailableException("Codex logout failed.");
            }

            lock (_stateGate)
            {
                _login = null;
            }
        }
        finally
        {
            if (generation is not null)
            {
                _runs.EndAuthenticationChange(generation.Value);
            }

            _operationGate.Release();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Process? process;
        CancellationTokenSource? lifetime;
        long? generation;
        lock (_stateGate)
        {
            process = _loginProcess;
            lifetime = _loginLifetime;
            generation = _authenticationGeneration;
            _loginProcess = null;
            _loginLifetime = null;
            _authenticationGeneration = null;
        }

        lifetime?.Cancel();
        if (process is not null)
        {
            ContainerRuntime.KillProcessTree(process);
            process.Dispose();
        }

        lifetime?.Dispose();
        if (generation is not null)
        {
            _runs.EndAuthenticationChange(generation.Value);
        }

        _operationGate.Dispose();
    }

    private void CaptureLoginOutput(
        Process process,
        DeviceLoginOutput output,
        string? line,
        TaskCompletionSource<DeviceLogin> loginReady)
    {
        if (line is null)
        {
            return;
        }

        var login = output.Add(AnsiEscapePattern.Replace(line, string.Empty));
        if (login is null)
        {
            return;
        }

        lock (_stateGate)
        {
            if (!ReferenceEquals(_loginProcess, process))
            {
                return;
            }

            _login = login;
        }

        loginReady.TrySetResult(login);
    }

    private async Task MonitorLoginAsync(
        Process process,
        DeviceLoginOutput output,
        TaskCompletionSource<DeviceLogin> loginReady,
        CancellationTokenSource lifetime,
        long generation)
    {
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(lifetime.Token);
            process.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            lock (_stateGate)
            {
                timedOut = ReferenceEquals(_loginLifetime, lifetime);
            }

            ContainerRuntime.KillProcessTree(process);
        }

        DeviceLogin? terminalLogin = null;
        var ownsProcess = false;
        lock (_stateGate)
        {
            if (ReferenceEquals(_loginProcess, process))
            {
                ownsProcess = true;
                var current = _login ?? output.Current;
                var error = timedOut
                    ? "Codex device login expired before it was completed."
                    : process.ExitCode == 0
                        ? null
                        : "Codex device login failed.";
                terminalLogin = (current ?? new DeviceLogin(output.LoginId, string.Empty, string.Empty, DeviceLoginStatus.Pending)) with
                {
                    Status = error is null ? DeviceLoginStatus.Completed : DeviceLoginStatus.Failed,
                    Error = error
                };
                _login = terminalLogin;
                _loginProcess = null;
                _loginLifetime = null;
                _authenticationGeneration = null;
            }
        }

        if (!ownsProcess)
        {
            return;
        }

        if (terminalLogin!.VerificationUrl.Length == 0 || terminalLogin.UserCode.Length == 0)
        {
            loginReady.TrySetException(new CodexUnavailableException(
                "Codex did not provide a device-login URL and code."));
        }
        else
        {
            // The CLI can print the code and exit before asynchronous output callbacks
            // observe that this process became the active login.
            loginReady.TrySetResult(terminalLogin);
        }

        lifetime.Dispose();
        process.Dispose();
        _runs.EndAuthenticationChange(generation);
        _logger.LogInformation("Codex device login finished with status {Status}.", terminalLogin.Status);
    }

    private async Task<CliResult> ExecuteAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var startInfo = CreateStartInfo(arguments);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("Process did not start.");
            }
        }
        catch (Exception)
        {
            throw new CodexUnavailableException("The Codex CLI could not be started.");
        }

        var output = ReadLimitedAsync(process.StandardOutput, 64 * 1024);
        var error = ReadLimitedAsync(process.StandardError, 64 * 1024);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(
            _options.AuthenticationCommandTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            ContainerRuntime.KillProcessTree(process);
            throw new CodexUnavailableException("The Codex authentication command timed out.");
        }
        catch
        {
            ContainerRuntime.KillProcessTree(process);
            throw;
        }

        return new CliResult(process.ExitCode, await output, await error);
    }

    private ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        Directory.CreateDirectory(_codexHome);
        Directory.CreateDirectory(Path.Combine(_codexHome, ".cache"));
        var temporaryDirectory = Path.Combine(_codexHome, ".tmp");
        Directory.CreateDirectory(temporaryDirectory);
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            WorkingDirectory = _codexHome,
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

        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add("cli_auth_credentials_store=\"file\"");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private void ThrowIfDisposed() =>
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

    private static async Task<string> ReadLimitedAsync(StreamReader reader, int maximumCharacters)
    {
        var buffer = new char[4096];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer);
            if (read == 0)
            {
                return result.ToString();
            }

            if (result.Length < maximumCharacters)
            {
                result.Append(buffer, 0, Math.Min(read, maximumCharacters - result.Length));
            }
        }
    }

    private sealed class DeviceLoginOutput(string loginId)
    {
        private readonly object _gate = new();
        private string? _verificationUrl;
        private string? _userCode;

        public string LoginId { get; } = loginId;

        public DeviceLogin? Current
        {
            get
            {
                lock (_gate)
                {
                    return CreateLogin();
                }
            }
        }

        public DeviceLogin? Add(string line)
        {
            lock (_gate)
            {
                _verificationUrl ??= VerificationUrlPattern.Match(line) is { Success: true } url
                    ? url.Value.TrimEnd('.', ',', ';')
                    : null;
                _userCode ??= DeviceCodePattern.Match(line) is { Success: true } code
                    ? code.Value
                    : null;
                return CreateLogin();
            }
        }

        private DeviceLogin? CreateLogin() =>
            _verificationUrl is not null && _userCode is not null
                ? new DeviceLogin(LoginId, _verificationUrl, _userCode, DeviceLoginStatus.Pending)
                : null;
    }

    private sealed record CliResult(int ExitCode, string StandardOutput, string StandardError);
}
