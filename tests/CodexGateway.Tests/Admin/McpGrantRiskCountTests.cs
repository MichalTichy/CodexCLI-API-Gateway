using CodexGateway.App.Components.Pages.Admin.Projects.Models;

namespace CodexGateway.Tests.Admin;

public sealed class McpGrantRiskCountTests
{
    [Fact]
    public void Counts_cover_only_allowed_tools_and_keep_unknown_risk_separate()
    {
        var grant = new McpGrantEditorModel
        {
            Id = "server",
            Name = "Server",
            Tools =
            [
                new() { Name = "delete", Enabled = true, Annotations = new() { DestructiveHint = true } },
                new() { Name = "blocked-delete", Enabled = false, Annotations = new() { DestructiveHint = true } },
                new() { Name = "read", Enabled = true, Annotations = new() { ReadOnlyHint = true, DestructiveHint = true } },
                new() { Name = "write", Enabled = true, Annotations = new() { ReadOnlyHint = false, DestructiveHint = false } },
                new() { Name = "unknown", Enabled = true },
                new() { Name = "incomplete", Enabled = true, Annotations = new() { DestructiveHint = false } },
                new() { Name = "blocked-unknown", Enabled = false }
            ]
        };

        Assert.Equal(1, grant.AllowedDestructiveToolCount);
        Assert.Equal(2, grant.AllowedUnknownRiskToolCount);

        grant.Tools[0].Enabled = false;
        grant.Tools[1].Enabled = true;
        grant.Tools[4].Enabled = false;
        Assert.Equal(1, grant.AllowedDestructiveToolCount);
        Assert.Equal(1, grant.AllowedUnknownRiskToolCount);
    }
}
