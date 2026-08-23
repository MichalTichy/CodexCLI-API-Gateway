using System.Diagnostics;

namespace CodexGateway.Infrastructure.Codex;

internal sealed class ContainerRunHandle(
    ContainerRuntime runtime,
    Process engineProcess,
    string containerName)
{
    private readonly object _gate = new();
    private Task<bool>? _termination;
    private int _containerObserved;

    public void MarkContainerObserved() => Volatile.Write(ref _containerObserved, 1);

    public Task<bool> TerminateAsync()
    {
        lock (_gate)
        {
            return _termination ??= TerminateCoreAsync();
        }
    }

    public void TerminateBlocking() => TerminateAsync().GetAwaiter().GetResult();

    private async Task<bool> TerminateCoreAsync()
    {
        ContainerRuntime.KillProcessTree(engineProcess);
        return await runtime.ForceRemoveAsync(
            containerName,
            retryMissingContainer: Volatile.Read(ref _containerObserved) == 0);
    }
}
