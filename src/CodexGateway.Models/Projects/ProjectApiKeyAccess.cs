namespace CodexGateway.Models.Projects;

public sealed record ProjectApiKeyAccess
{
    public required string ApiKeyId { get; init; }

    public WebSearchMode WebSearchMode { get; init; } = WebSearchMode.Disabled;

    public List<ProjectMcpAssignment> McpServers { get; init; } = [];
}
