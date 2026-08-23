using CodexGateway.Logic.Security;
using CodexGateway.Models;

namespace CodexGateway.App.Components.Pages.Admin.Projects.Models;

internal sealed class ApiKeyAccessEditorModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool HasProjectAccess { get; set; }

    public List<McpGrantEditorModel> Servers { get; init; } = [];

    public static ApiKeyAccessEditorModel From(
        ApiKeyIdentity key,
        ProjectApiKeyAccess? access,
        IReadOnlyList<McpServerDefinition> servers)
    {
        var assignments = (access?.McpServers ?? [])
            .ToDictionary(assignment => assignment.ServerId, StringComparer.Ordinal);
        return new ApiKeyAccessEditorModel
        {
            Id = key.Id,
            Name = key.Name,
            HasProjectAccess = access is not null,
            Servers = servers
                .Select(server => McpGrantEditorModel.From(
                    server,
                    assignments.GetValueOrDefault(server.Id)))
                .ToList()
        };
    }

    public ProjectApiKeyAccess ToDefinition() => new()
    {
        ApiKeyId = Id,
        McpServers = Servers
            .Where(server => server.Granted && server.CatalogEnabled)
            .Select(server => server.ToDefinition())
            .ToList()
    };
}
