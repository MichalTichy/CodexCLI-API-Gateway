using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Shared.Infrastructure.IoC.Installers;

public interface IInstaller
{
    void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment);
}
