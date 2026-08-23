using CodexGateway.Models;

namespace CodexGateway.App.Components.Pages.Admin.Projects.Models;

internal sealed class McpGrantEditorModel
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public bool CatalogEnabled { get; init; }

    public bool Granted { get; set; }

    public bool Required { get; set; }

    public List<ToolGrantEditorModel> Tools { get; init; } = [];

    public static McpGrantEditorModel From(
        McpServerDefinition server,
        ProjectMcpAssignment? assignment)
    {
        var enabledTools = (assignment?.EnabledTools ?? []).ToHashSet(StringComparer.Ordinal);
        return new McpGrantEditorModel
        {
            Id = server.Id,
            Name = server.Name,
            CatalogEnabled = server.Enabled,
            Granted = assignment is not null && server.Enabled,
            Required = assignment?.Required == true,
            Tools = (server.AvailableTools ?? [])
                .Select(tool => new ToolGrantEditorModel
                {
                    Name = tool,
                    Enabled = enabledTools.Contains(tool)
                })
                .ToList()
        };
    }

    public ProjectMcpAssignment ToDefinition() => new()
    {
        ServerId = Id,
        Required = Required,
        EnabledTools = Tools.Where(tool => tool.Enabled).Select(tool => tool.Name).ToList()
    };
}
