using CodexGateway.App.Components.Pages.Admin.Shared;
using CodexGateway.App.Components.Pages.Admin.Shared.Models;
using CodexGateway.Logic.Codex.Models;

namespace CodexGateway.App.Components.Pages.Admin.Projects.Models;

internal sealed class ToolGrantEditorModel
{
    public required string Name { get; init; }

    public bool Enabled { get; set; }

    public string? Description { get; set; }

    public string? InputSchema { get; set; }

    public bool? AvailableInDiscovery { get; set; }

    public bool SchemaChanged { get; set; }

    public McpToolAnnotations? Annotations { get; set; }

    public ToolRisk Risk => ToolRiskPresentation.Classify(Annotations);
}
