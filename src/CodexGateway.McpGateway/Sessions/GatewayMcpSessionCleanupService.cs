using Microsoft.Extensions.Hosting;

namespace CodexGateway.McpGateway.Sessions;

internal sealed class GatewayMcpSessionCleanupService(GatewayMcpSessionManager sessions)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await sessions.RemoveExpiredAsync();
        }
    }
}
