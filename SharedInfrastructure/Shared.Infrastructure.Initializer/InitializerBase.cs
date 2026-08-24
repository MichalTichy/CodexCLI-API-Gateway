using Microsoft.Extensions.Configuration;

namespace Shared.Infrastructure.Initializer;

public abstract class InitializerBase : IInitializer
{
    public bool DidRun { get; private set; }

    public abstract int Priority { get; }

    public abstract InitializerTrigger Trigger { get; }

    public abstract bool RunOnlyInLeaderInstance { get; }

    public virtual string Name => GetType().Name;

    public async Task InitializeAsync(IConfiguration configuration)
    {
        if (DidRun)
        {
            throw new InitializerWasAlreadyExecutedException();
        }

        await RunInitializationLogicAsync();
        DidRun = true;
    }

    protected abstract Task RunInitializationLogicAsync();
}
