using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure.IoC.Installers;

namespace Shared.Infrastructure.Initializer;

public sealed class InitializerInstaller : IInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddSingleton<InitializerManager>();
    }
}
