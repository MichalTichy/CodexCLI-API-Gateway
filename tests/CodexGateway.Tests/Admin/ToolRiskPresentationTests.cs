using CodexGateway.App.Components.Pages.Admin.Shared;
using CodexGateway.App.Components.Pages.Admin.Shared.Models;
using CodexGateway.Logic.Codex.Models;

namespace CodexGateway.Tests.Admin;

public sealed class ToolRiskPresentationTests
{
    [Fact]
    public void Missing_or_incomplete_annotations_are_not_presented_as_safe()
    {
        Assert.Equal(ToolRisk.Unknown, ToolRiskPresentation.Classify(null));
        Assert.Equal(ToolRisk.Unknown, ToolRiskPresentation.Classify(new()));
        Assert.Equal(ToolRisk.Unknown, ToolRiskPresentation.Classify(new() { DestructiveHint = false }));
        Assert.Equal(ToolRisk.Unknown, ToolRiskPresentation.Classify(new() { ReadOnlyHint = false }));
    }

    [Fact]
    public void Explicit_destructive_and_write_hints_have_different_risk_levels()
    {
        Assert.Equal(ToolRisk.Destructive, ToolRiskPresentation.Classify(new() { DestructiveHint = true }));
        Assert.Equal(ToolRisk.Write, ToolRiskPresentation.Classify(new() { ReadOnlyHint = false, DestructiveHint = false }));
    }

    [Fact]
    public void Read_only_hint_takes_precedence_over_inapplicable_destructive_hint()
    {
        Assert.Equal(ToolRisk.ReadOnly, ToolRiskPresentation.Classify(new() { ReadOnlyHint = true, DestructiveHint = true }));
    }

    [Fact]
    public void Risk_badges_have_resolved_localized_labels()
    {
        Assert.Equal("Destructive", ToolRiskPresentation.Label(ToolRisk.Destructive));
        Assert.Equal("Risk unknown", ToolRiskPresentation.Label(ToolRisk.Unknown));
    }
}
