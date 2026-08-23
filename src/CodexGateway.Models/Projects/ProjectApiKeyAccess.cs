namespace CodexGateway.Models.Projects;

public sealed record ProjectApiKeyAccess
{
    public required string ApiKeyId { get; init; }

    public List<ProjectMcpAssignment> McpServers { get; init; } = [];
}
