namespace CodexGateway.Models;

public sealed record ProjectMcpAssignment
{
    public required string ServerId { get; init; }

    public bool Required { get; init; }

    public List<string> EnabledTools { get; init; } = [];

    // When omitted, visibility defaults to every enabled tool. An explicit list
    // may be broader than EnabledTools for planning.
    public List<string>? VisibleTools { get; init; }
}
