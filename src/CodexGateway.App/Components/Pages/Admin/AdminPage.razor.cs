using CodexGateway.App.Configuration;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace CodexGateway.App.Components.Pages;

public partial class AdminPage : ComponentBase
{
    [Inject]
    private IOptions<AdminUiOptions> AdminOptions { get; set; } = null!;

    [SupplyParameterFromQuery(Name = "error")]
    public string? LoginError { get; set; }

    [SupplyParameterFromQuery(Name = "logout")]
    public string? LogoutStatus { get; set; }
}
