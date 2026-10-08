using CodexGateway.App.Components.Pages.Admin.Shared.Models;
using CodexGateway.Logic.Codex.Models;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.Shared;

public partial class ToolRiskBadge : ComponentBase
{
    [Parameter] public McpToolAnnotations? Annotations { get; set; }
    private ToolRisk Risk => ToolRiskPresentation.Classify(Annotations);
}
