using CodexGateway.Models.ApiKeys;
using CodexGateway.Models.McpServers;
using CodexGateway.Models.Projects;
using Shared.Infrastructure.Persistence;

namespace CodexGateway.Models.Gateway;

public sealed class GatewayState : IItemWithId
{
    public const string DocumentId = "gateway";

    public string Id { get; init; } = DocumentId;

    public List<ApiKeyDefinition> ApiKeys { get; set; } = [];

    public List<ProjectDefinition> Projects { get; set; } = [];

    public List<McpServerDefinition> McpServers { get; set; } = [];
}
