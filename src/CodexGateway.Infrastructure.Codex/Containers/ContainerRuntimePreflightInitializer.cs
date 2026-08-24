using Microsoft.Extensions.Hosting;

namespace CodexGateway.Infrastructure.Codex.Containers;

public sealed class ContainerRuntimePreflightService(ContainerRuntime runtime) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => runtime.PreflightAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
