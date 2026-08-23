namespace CodexGateway.Infrastructure.Storage;

internal readonly record struct ArtifactUsage(int FileCount, long TotalBytes, long LargestFileBytes);
