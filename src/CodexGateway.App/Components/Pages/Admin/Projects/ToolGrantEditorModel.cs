namespace CodexGateway.App.Components.Admin;

internal sealed class ToolGrantEditorModel
{
    public required string Name { get; init; }

    public bool Visible { get; set; }

    public bool Enabled { get; set; }
}
