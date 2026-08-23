namespace CodexGateway.Models;

public sealed record FileRecord
{
    public required string Id { get; init; }

    public required string FileName { get; init; }

    public required string StoredName { get; init; }

    public required long Bytes { get; init; }

    public required string Purpose { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public string? ProjectId { get; init; }

    // Projectless files are private to the configured global API key that created them.
    // Project files are not scoped to an API key.
    public string? ApiKeyId { get; init; }
}
