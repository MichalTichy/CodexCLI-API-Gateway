using CodexGateway.Logic.Errors;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Admin;

public abstract class AdminComponentBase : ComponentBase, IAsyncDisposable
{
    public CancellationTokenSource PageCancellationTokenSource { get; } = new();

    protected CancellationToken PageCancellationToken => PageCancellationTokenSource.Token;

    protected bool IsBusy { get; private set; }

    protected string Status { get; private set; } = string.Empty;

    protected AdminStatusKind StatusKind { get; private set; }

    protected async Task RunAsync(
        Func<Task> operation,
        Func<Task> onSuccess,
        ILogger logger,
        string workingMessage,
        string errorMessage)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        SetStatus(workingMessage);
        try
        {
            await operation();
            SetStatus(string.Empty);
            await onSuccess();
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                logger.LogError(exception, "{ErrorMessage}", errorMessage);
            }

            SetStatus(AdminText.Describe(exception), AdminStatusKind.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected void SetStatus(string message, AdminStatusKind kind = AdminStatusKind.None)
    {
        Status = message;
        StatusKind = kind;
    }

    public virtual async ValueTask DisposeAsync()
    {
        await PageCancellationTokenSource.CancelAsync();
        PageCancellationTokenSource.Dispose();
    }
}
