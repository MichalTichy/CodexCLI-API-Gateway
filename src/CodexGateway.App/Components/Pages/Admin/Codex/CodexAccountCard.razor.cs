using CodexGateway.Logic.Codex;
using CodexGateway.Logic.UseCases.CodexAuthentication;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.Codex;

public partial class CodexAccountCard : AdminComponentBase
{
    private bool _confirmLogout;

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<CodexAccountCard> Logger { get; set; } = null!;

    [Parameter]
    public CodexAccountStatus? Account { get; set; }

    [Parameter]
    public DeviceLogin? Login { get; set; }

    [Parameter]
    public string? LoadError { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    private Task RetryAsync() => RunAsync(
        () => Task.CompletedTask,
        () => OnChanged.InvokeAsync("Codex account status refreshed."),
        Logger,
        "Working…",
        "Unexpected failure while changing Codex authentication.");

    private Task StartLoginAsync() => RunAsync(
        () => Sender.Send(new StartCodexDeviceLoginUseCase(), PageCancellationToken),
        () => NotifyChangedAsync("Codex device login started."),
        Logger,
        "Working…",
        "Unexpected failure while changing Codex authentication.");

    private Task CancelLoginAsync() => RunAsync(
        () => Sender.Send(new CancelCodexDeviceLoginUseCase(), PageCancellationToken),
        () => NotifyChangedAsync("Codex device login cancelled."),
        Logger,
        "Working…",
        "Unexpected failure while changing Codex authentication.");

    private Task LogoutAsync() => RunAsync(
        () => Sender.Send(new LogoutCodexAccountUseCase(), PageCancellationToken),
        () => NotifyChangedAsync("The gateway has been logged out of Codex."),
        Logger,
        "Working…",
        "Unexpected failure while changing Codex authentication.");

    private void ConfirmLogout() => _confirmLogout = true;

    private void CancelLogout() => _confirmLogout = false;

    private Task NotifyChangedAsync(string message)
    {
        _confirmLogout = false;
        return OnChanged.InvokeAsync(message);
    }
}
