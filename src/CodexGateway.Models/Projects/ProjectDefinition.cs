namespace CodexGateway.Models.Projects;

public sealed record ProjectDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool Enabled { get; init; } = true;

    public string? RunnerImage { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public List<ProjectApiKeyAccess> ApiKeyAccess { get; init; } = [];
}
