namespace CodexGateway.Models;

public sealed record ApiKeyDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Key { get; init; }
}
