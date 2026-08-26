using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace CodexGateway.App.Components.Pages.Admin;

public partial class AdminPage : ComponentBase
{
    private bool _showPassword;

    [Inject]
    private IOptions<AdminUiOptions> AdminOptions { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "error")]
    public string? LoginError { get; set; }

    [SupplyParameterFromQuery(Name = "logout")]
    public string? LogoutStatus { get; set; }

    private void TogglePasswordVisibility() => _showPassword = !_showPassword;
}
