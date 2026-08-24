using CodexGateway.Logic.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Infrastructure.FileStorage.Composition;

public sealed class FileStorageInfrastructureInstaller : IHighPriorityInstaller
{
    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.TryAddSingleton<StoragePaths>();

        services.TryAddSingleton<ProjectStorageManager>();
        services.TryAddSingleton<IProjectStorageManager>(provider =>
            provider.GetRequiredService<ProjectStorageManager>());

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
