using Shared.Infrastructure.Initializer;

namespace CodexGateway.Infrastructure.Codex.Containers;

public sealed class ContainerRuntimePreflightInitializer(ContainerRuntime runtime) : InitializerBase
{
    public override int Priority => 0;

    public override InitializerTrigger Trigger => InitializerTrigger.OnStartup;

    public override bool RunOnlyInLeaderInstance => false;

    protected override Task RunInitializationLogicAsync() =>
        runtime.PreflightAsync(CancellationToken.None);
}
