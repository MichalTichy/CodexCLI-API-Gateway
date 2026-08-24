using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodexGateway.Infrastructure.Storage.Files;

public sealed class TemporaryFileCleanupService(FileStore files, ILogger<TemporaryFileCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try
            {
                await files.DeleteExpiredTemporaryFilesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Temporary file cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
