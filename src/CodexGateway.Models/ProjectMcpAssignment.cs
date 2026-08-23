namespace CodexGateway.Models;

public sealed record ProjectMcpAssignment
{
    public required string ServerId { get; init; }

    public bool Required { get; init; }

    public List<string> EnabledTools { get; init; } = [];
}
