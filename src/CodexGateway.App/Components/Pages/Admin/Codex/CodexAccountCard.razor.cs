using CodexGateway.Logic.Codex;
using CodexGateway.Logic.UseCases.CodexAuthentication;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CodexGateway.App.Components.Pages.Admin.Codex;

public partial class CodexAccountCard : AdminComponentBase
{
    private bool _confirmLogout;
    private string? _activeLoginId;
    private bool _codeCopied;

    private string LoginExpiryLabel
    {
        get
        {
            if (Login?.ExpiresAt is not { } expiresAt)
            {
                return "Waiting for authentication…";
            }

            var remaining = expiresAt - DateTimeOffset.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return "Code is expiring; wait for the status check or generate a new code.";
            }

            return $"Waiting for authentication… Expires in {(int)remaining.TotalMinutes}:{remaining.Seconds:00}.";
        }
    }

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<CodexAccountCard> Logger { get; set; } = null!;

    [Inject]
    private IJSRuntime JavaScript { get; set; } = null!;

    [Parameter]
    public CodexAccountStatus? Account { get; set; }

    [Parameter]
    public DateTimeOffset? LastVerifiedAt { get; set; }

    private string LastVerifiedLabel => LastVerifiedAt is null
        ? "Checked just now"
        : $"Last verified {LastVerifiedAt.Value.ToLocalTime():HH:mm:ss}";

    [Parameter]
    public DeviceLogin? Login { get; set; }

    [Parameter]
    public string? LoadError { get; set; }

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    protected override void OnParametersSet()
    {
        if (!string.Equals(_activeLoginId, Login?.LoginId, StringComparison.Ordinal))
        {
            _activeLoginId = Login?.LoginId;
            _codeCopied = false;
        }
    }

    private Task RetryAsync() => RunAsync(
        () => Task.CompletedTask,
        () => OnChanged.InvokeAsync("Codex account status refreshed."),
        Logger,
        "Working…",
        "Unexpected failure while changing Codex authentication.");

    private Task StartLoginAsync() => RunAsync(
        () => Sender.Send(new StartCodexDeviceLoginUseCase(), PageCancellationToken),
        () => NotifyChangedAsync(string.Empty),
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

    private async Task CopyCodeAsync()
    {
        if (Login is null)
        {
            return;
        }

        await JavaScript.InvokeVoidAsync("navigator.clipboard.writeText", Login.UserCode);
        _codeCopied = true;
    }

    private Task NotifyChangedAsync(string message)
    {
        _confirmLogout = false;
        return OnChanged.InvokeAsync(message);
    }
}
