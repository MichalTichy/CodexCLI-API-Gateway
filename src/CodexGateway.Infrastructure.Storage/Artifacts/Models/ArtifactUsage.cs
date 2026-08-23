namespace CodexGateway.Infrastructure.Storage.Artifacts.Models;

internal readonly record struct ArtifactUsage(int FileCount, long TotalBytes, long LargestFileBytes);
