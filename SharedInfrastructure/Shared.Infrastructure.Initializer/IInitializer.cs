using Microsoft.Extensions.Configuration;

namespace Shared.Infrastructure.Initializer;

public interface IInitializer
{
    int Priority { get; }

    InitializerTrigger Trigger { get; }

    bool RunOnlyInLeaderInstance { get; }

    string Name { get; }

    Task InitializeAsync(IConfiguration configuration);
}
