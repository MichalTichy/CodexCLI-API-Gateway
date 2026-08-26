using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.Shared;

public partial class ContextHelp : ComponentBase
{
    private bool _open;

    [Parameter, EditorRequired]
    public string Title { get; set; } = string.Empty;

    [Parameter, EditorRequired]
    public RenderFragment ChildContent { get; set; } = null!;

    private void Toggle() => _open = !_open;
}
