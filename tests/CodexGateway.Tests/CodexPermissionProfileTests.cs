using System.Text.Json;
using CodexGateway.Infrastructure.Codex;
namespace CodexGateway.Tests;

public sealed class CodexPermissionProfileTests
{
    [Fact]
    public void FileSystemProfileExposesOnlyMinimalRuntimeAndTheRunWorkspace()
    {
        var table = ContainerCodexRunner.BuildFileSystemPermissionTable();

        Assert.Contains($"{JsonSerializer.Serialize(":minimal")}=\"read\"", table, StringComparison.Ordinal);
        Assert.Contains($"{JsonSerializer.Serialize(":tmpdir")}=\"write\"", table, StringComparison.Ordinal);
        Assert.Contains($"{JsonSerializer.Serialize(":slash_tmp")}=\"write\"", table, StringComparison.Ordinal);
        Assert.Contains(
            $"{JsonSerializer.Serialize(":workspace_roots")}=\"write\"",
            table,
            StringComparison.Ordinal);
        Assert.DoesNotContain($"{JsonSerializer.Serialize(":root")}=\"read\"", table, StringComparison.Ordinal);
    }
}
