using Shared.Infrastructure.Persistence;

namespace CodexGateway.Models;

public sealed class GatewayState : IItemWithId
{
    public const string DocumentId = "gateway";

    public string Id { get; init; } = DocumentId;

    public List<ApiKeyDefinition> ApiKeys { get; set; } = [];

    public List<ProjectDefinition> Projects { get; set; } = [];

    public List<McpServerDefinition> McpServers { get; set; } = [];
}
