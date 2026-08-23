using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.IoC;

public interface IInstaller
{
    void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment);
}
