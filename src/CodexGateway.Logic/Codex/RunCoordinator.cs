using System.Collections.Concurrent;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Codex;

public sealed class RunCoordinator
{
    private readonly SemaphoreSlim _global;
    private readonly SemaphoreSlim _capacity;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projects = new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _timeout;
    private readonly object _admissionGate = new();
    private int _activeRuns;
    private int _admittedRuns;
    private bool _authenticationChangeInProgress;
    private long _authenticationChangeGeneration;

    public RunCoordinator(IOptions<GatewayOptions> options)
    {
        var limits = options.Value.Limits;
        _global = new SemaphoreSlim(limits.MaxConcurrent, limits.MaxConcurrent);
        _capacity = new SemaphoreSlim(limits.MaxConcurrent + limits.MaxQueued, limits.MaxConcurrent + limits.MaxQueued);
        _timeout = TimeSpan.FromSeconds(limits.TimeoutSeconds);
    }

    public int ActiveRuns => Volatile.Read(ref _activeRuns);

    public int AdmittedRuns
    {
        get
        {
            lock (_admissionGate)
            {
                return _admittedRuns;
            }
        }
    }

    public long BeginAuthenticationChange()
    {
        lock (_admissionGate)
        {
            if (_authenticationChangeInProgress)
            {
                throw new GatewayException(
                    GatewayErrorCategory.Conflict,
                    409,
                    "authentication_change_in_progress",
                    "Codex authentication is already being updated.");
            }

            if (_admittedRuns > 0)
            {
                throw new GatewayException(
                    GatewayErrorCategory.Conflict,
                    409,
                    "runs_active",
                    "Codex authentication cannot change while runs are active or queued.");
            }

            _authenticationChangeInProgress = true;
            return ++_authenticationChangeGeneration;
        }
    }

    public void EndAuthenticationChange(long generation)
    {
        lock (_admissionGate)
        {
            if (_authenticationChangeInProgress && _authenticationChangeGeneration == generation)
            {
                _authenticationChangeInProgress = false;
            }
        }
    }

    public async Task<T> ExecuteAsync<T>(string? projectId, Func<CancellationToken, Task<T>> action, CancellationToken requestCancellation)
    {
        if (!await _capacity.WaitAsync(0, requestCancellation))
        {
            throw new QueueFullException();
        }

        SemaphoreSlim? projectGate = null;
        var admitted = false;
        var globalAcquired = false;
        var projectAcquired = false;
        try
        {
            AdmitRun();
            admitted = true;

            if (projectId is not null)
            {
                projectGate = GetProjectGate(projectId);
                await projectGate.WaitAsync(requestCancellation);
                projectAcquired = true;
            }

            await _global.WaitAsync(requestCancellation);
            globalAcquired = true;
            Interlocked.Increment(ref _activeRuns);

            using var timeout = new CancellationTokenSource(_timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, timeout.Token);
            try
            {
                return await action(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !requestCancellation.IsCancellationRequested)
            {
                throw new RunTimedOutException();
            }
        }
        finally
        {
            if (globalAcquired)
            {
                Interlocked.Decrement(ref _activeRuns);
                _global.Release();
            }

            if (projectAcquired)
            {
                projectGate!.Release();
            }

            if (admitted)
            {
                ReleaseRunAdmission();
            }

            _capacity.Release();
        }
    }

    public async Task<T> ExecuteProjectOperationAsync<T>(
        string projectId,
        Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        var projectGate = GetProjectGate(projectId);
        await projectGate.WaitAsync(cancellationToken);
        try
        {
            return await action(cancellationToken);
        }
        finally
        {
            projectGate.Release();
        }
    }

    public async Task ExecuteProjectOperationAsync(
        string projectId,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        await ExecuteProjectOperationAsync(
            projectId,
            async operationCancellation =>
            {
                await action(operationCancellation);
                return true;
            },
            cancellationToken);
    }

    private SemaphoreSlim GetProjectGate(string projectId) =>
        _projects.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));

    private void AdmitRun()
    {
        lock (_admissionGate)
        {
            if (_authenticationChangeInProgress)
            {
                throw new CodexUnavailableException("Codex authentication is being updated. Try again after it completes.");
            }

            _admittedRuns++;
        }
    }

    private void ReleaseRunAdmission()
    {
        lock (_admissionGate)
        {
            _admittedRuns--;
        }
    }
}
