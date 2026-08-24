using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Storage.Artifacts;

public sealed class RunArtifactQuotaMonitor
{
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(100);
    private readonly ArtifactOptions _limits;
    private readonly TimeSpan _interval;

    public RunArtifactQuotaMonitor(IOptions<GatewayOptions> options)
        : this(options.Value.Artifacts, DefaultInterval)
    {
    }

    internal RunArtifactQuotaMonitor(ArtifactOptions limits, TimeSpan interval)
    {
        _limits = limits;
        _interval = interval;
    }

    public void Check(string runRootPath, CancellationToken cancellationToken) =>
        _ = ArtifactQuotaGuard.Measure(runRootPath, _limits, cancellationToken);

    public async Task MonitorAsync(
        string runRootPath,
        Action terminateProcess,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                Check(runRootPath, cancellationToken);
            }
            catch (GatewayException exception) when (exception.Code == "artifact_limit_exceeded")
            {
                terminateProcess();
                throw;
            }

            await Task.Delay(_interval, cancellationToken);
        }
    }
}
