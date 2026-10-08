using CodexGateway.App.Components.Pages.Admin.Shared.Models;
using CodexGateway.Logic.Codex.Models;

namespace CodexGateway.App.Components.Pages.Admin.Shared;

internal static class ToolRiskPresentation
{
    public static ToolRisk Classify(McpToolAnnotations? annotations) => annotations switch
    {
        { ReadOnlyHint: true } => ToolRisk.ReadOnly,
        { DestructiveHint: true } => ToolRisk.Destructive,
        { ReadOnlyHint: false, DestructiveHint: false } => ToolRisk.Write,
        _ => ToolRisk.Unknown
    };

    public static string CssClass(ToolRisk risk) => $"risk-{risk.ToString().ToLowerInvariant()}";
    public static string Label(ToolRisk risk) => AdminUx.Text(risk.ToString());
    public static string Symbol(ToolRisk risk) => risk switch
    {
        ToolRisk.Destructive => "⚠",
        ToolRisk.Write => "✎",
        ToolRisk.ReadOnly => "◉",
        _ => "?"
    };
}
