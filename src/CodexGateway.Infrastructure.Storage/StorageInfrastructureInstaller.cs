using CodexGateway.IoC;
using CodexGateway.Logic.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Infrastructure.Storage;

public sealed class StorageInfrastructureInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.TryAddSingleton<StoragePaths>();

        services.TryAddSingleton<JsonGatewayConfigurationRepository>();
        services.TryAddSingleton<IGatewayConfigurationRepository>(provider =>
            provider.GetRequiredService<JsonGatewayConfigurationRepository>());

        services.TryAddSingleton<ProjectStorage>();
        services.TryAddSingleton<IProjectStorage>(provider =>
            provider.GetRequiredService<ProjectStorage>());

        services.TryAddSingleton<FileStore>();
        services.TryAddSingleton<IFileStore>(provider =>
            provider.GetRequiredService<FileStore>());

        services.TryAddSingleton<WorkspaceManager>();
        services.TryAddSingleton<IWorkspaceManager>(provider =>
            provider.GetRequiredService<WorkspaceManager>());

        services.TryAddSingleton<RunArtifactQuotaMonitor>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IHostedService, TemporaryFileCleanupService>());
    }
}
