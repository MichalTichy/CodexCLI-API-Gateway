using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.IoC.Installers;

namespace Shared.Infrastructure.LeaderElection;

public sealed class LeaderElectionInstaller : ILowPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.TryAddSingleton<ILeaderElection, LeaderElectionSingleInstance>();
    }
}
