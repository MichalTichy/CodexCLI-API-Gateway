namespace CodexGateway.Infrastructure.FileStorage.Artifacts.Models;

internal readonly record struct ArtifactUsage(int FileCount, long TotalBytes, long LargestFileBytes);
