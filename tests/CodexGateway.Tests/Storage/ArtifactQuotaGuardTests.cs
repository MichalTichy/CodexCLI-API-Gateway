using System.Diagnostics;
using CodexGateway.Infrastructure.FileStorage;
using CodexGateway.Logic.Configuration;
using CodexGateway.Logic.Errors;

namespace CodexGateway.Tests.Storage;

public sealed class ArtifactQuotaGuardTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "codex-gateway-artifact-quota-tests",
        Guid.NewGuid().ToString("N"));

    public ArtifactQuotaGuardTests()
    {
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void Measure_enforces_file_count()
    {
        File.WriteAllText(Path.Combine(_root, "one.txt"), "one");
        File.WriteAllText(Path.Combine(_root, "two.txt"), "two");

        var exception = Assert.Throws<GatewayException>(() =>
            ArtifactQuotaGuard.Measure(_root, Limits(maxFiles: 1), CancellationToken.None));

        Assert.Equal("artifact_limit_exceeded", exception.Code);
    }

    [Fact]
    public void Measure_enforces_per_file_size()
    {
        using var stream = new FileStream(
            Path.Combine(_root, "large.bin"),
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);
        stream.SetLength(2L * 1024L * 1024L);

        Assert.Throws<GatewayException>(() =>
            ArtifactQuotaGuard.Measure(_root, Limits(maxFileMegabytes: 1), CancellationToken.None));
    }

    [Fact]
    public void Measure_enforces_total_size()
    {
        CreateSizedFile("one.bin", 768L * 1024L);
        CreateSizedFile("two.bin", 768L * 1024L);

        Assert.Throws<GatewayException>(() =>
            ArtifactQuotaGuard.Measure(
                _root,
                Limits(maxFileMegabytes: 1, maxTotalMegabytes: 1),
                CancellationToken.None));
    }

    [Fact]
    public void Measure_does_not_follow_directory_links()
    {
        var outside = Path.Combine(Path.GetDirectoryName(_root)!, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        try
        {
            using (var stream = File.Create(Path.Combine(outside, "outside.bin")))
            {
                stream.SetLength(2L * 1024L * 1024L);
            }

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "linked"), outside);
            }
            catch (Exception exception) when (OperatingSystem.IsWindows() && exception is IOException or UnauthorizedAccessException)
            {
                return;
            }

            var usage = ArtifactQuotaGuard.Measure(
                _root,
                Limits(maxFileMegabytes: 1, maxTotalMegabytes: 1),
                CancellationToken.None);

            Assert.Equal(1, usage.FileCount);
            Assert.Equal(0, usage.TotalBytes);
        }
        finally
        {
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task Monitor_terminates_and_throws_as_soon_as_a_snapshot_exceeds_limits()
    {
        using (var stream = File.Create(Path.Combine(_root, "large.bin")))
        {
            stream.SetLength(2L * 1024L * 1024L);
        }

        var terminationCount = 0;
        var monitor = new RunArtifactQuotaMonitor(
            Limits(maxFileMegabytes: 1),
            TimeSpan.FromMilliseconds(1));

        var exception = await Assert.ThrowsAsync<GatewayException>(() =>
            monitor.MonitorAsync(_root, () => Interlocked.Increment(ref terminationCount), CancellationToken.None));

        Assert.Equal("artifact_limit_exceeded", exception.Code);
        Assert.Equal(1, terminationCount);
    }

    [Fact]
    public async Task Measure_does_not_block_on_a_fifo()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var fifo = Path.Combine(_root, "pipe");
        using (var process = Process.Start(new ProcessStartInfo("mkfifo", fifo) { UseShellExecute = false })!)
        {
            await process.WaitForExitAsync();
            Assert.Equal(0, process.ExitCode);
        }

        var measurement = Task.Run(() =>
            ArtifactQuotaGuard.Measure(_root, Limits(), CancellationToken.None));
        var usage = await measurement.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, usage.FileCount);
        Assert.Equal(0, usage.TotalBytes);
    }

    [Fact]
    public void Measure_counts_an_unreadable_linux_file_without_opening_its_content()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        var path = Path.Combine(_root, "unreadable.bin");
        CreateSizedFile("unreadable.bin", 2L * 1024L * 1024L);
        File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            Assert.Throws<GatewayException>(() =>
                ArtifactQuotaGuard.Measure(_root, Limits(maxFileMegabytes: 1), CancellationToken.None));
        }
        finally
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private void CreateSizedFile(string name, long bytes)
    {
        using var stream = File.Create(Path.Combine(_root, name));
        stream.SetLength(bytes);
    }

    private static ArtifactOptions Limits(
        int maxFiles = 100,
        int maxFileMegabytes = 10,
        int maxTotalMegabytes = 10) => new()
        {
            MaxFiles = maxFiles,
            MaxFileMegabytes = maxFileMegabytes,
            MaxTotalMegabytes = maxTotalMegabytes
        };
}
