using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.UseCases.CodexAuthentication;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;
using LumexUI.Common;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.App.Components.Pages.Admin.Dashboard;

public partial class AdminDashboard : AdminComponentBase
{
    private AdminSection _selectedSection;
    private IReadOnlyList<ProjectDefinition> _projects = [];
    private IReadOnlyList<McpServerDefinition> _servers = [];
    private IReadOnlyList<ApiKeyIdentity> _apiKeys = [];
    private CodexAccountStatus? _account;
    private DeviceLogin? _login;
    private Task? _pollTask;
    private string? _codexError;
    private bool _initialized;
    private bool _loading;
    private bool _refreshRequested;
    private bool _showRefreshSuccess;
    private bool _codexActionBusy;
    private Task? _refreshTask;

    private string SectionEyebrow => _selectedSection switch
    {
        AdminSection.Overview => "Gateway status",
        AdminSection.ApiKeys => "Access management",
        AdminSection.Projects => "Isolation boundaries",
        AdminSection.McpServers => "Tool catalog",
        AdminSection.Codex => "Runtime identity",
        _ => string.Empty
    };

    private string SectionTitle => _selectedSection switch
    {
        AdminSection.Overview => "Overview",
        AdminSection.ApiKeys => "API keys",
        AdminSection.Projects => "Projects",
        AdminSection.McpServers => "MCP servers",
        AdminSection.Codex => "Codex account",
        _ => string.Empty
    };

    private string SectionDescription => _selectedSection switch
    {
        AdminSection.Overview => "Gateway access and project configuration at a glance.",
        AdminSection.ApiKeys => "Create and revoke the global credentials consumers use to reach the gateway.",
        AdminSection.Projects => "Define project boundaries and grant each API key only the MCP tools it needs.",
        AdminSection.McpServers => "Manage the trusted HTTP and STDIO servers that projects can use.",
        AdminSection.Codex => "Authenticate the dedicated Codex identity shared by isolated run containers.",
        _ => string.Empty
    };

    private ThemeColor CodexStatusColor => _codexError is not null
        ? ThemeColor.Danger
        : _account?.Authenticated == true
            ? ThemeColor.Success
            : _login is { Status: DeviceLoginStatus.Pending }
                ? ThemeColor.Warning
                : ThemeColor.Primary;

    private string CodexStatusActionLabel => _codexActionBusy
        ? "Starting…"
        : _codexError is not null
            ? "Unavailable"
            : _account?.Authenticated == true
                ? "Connected"
                : _login is { Status: DeviceLoginStatus.Pending }
                    ? "View code"
                    : "Connect";

    private ThemeColor StatusColor => StatusKind switch
    {
        AdminStatusKind.Success => ThemeColor.Success,
        AdminStatusKind.Warning => ThemeColor.Warning,
        AdminStatusKind.Error => ThemeColor.Danger,
        _ => ThemeColor.Default
    };

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<AdminDashboard> Logger { get; set; } = null!;

    [Inject]
    private IReadOnlyRepository<GatewayState> Configuration { get; set; } = null!;

    protected override async Task OnInitializedAsync()
    {
        if (RendererInfo.IsInteractive)
        {
            await RefreshAsync(showSuccess: false);
        }
    }

    private Task RefreshAsync() => RefreshAsync(showSuccess: true);

    private Task RefreshAsync(bool showSuccess)
    {
        // A save can finish while a refresh is already reading. Request one more pass
        // so the older read cannot become the final state shown by the dashboard.
        _refreshRequested = true;
        _showRefreshSuccess |= showSuccess;

        if (_refreshTask is null || _refreshTask.IsCompleted)
        {
            _refreshTask = RunRefreshLoopAsync();
        }

        return _refreshTask;
    }

    private async Task RunRefreshLoopAsync()
    {
        while (_refreshRequested && !PageCancellationToken.IsCancellationRequested)
        {
            _refreshRequested = false;
            var showSuccess = _showRefreshSuccess;
            _showRefreshSuccess = false;
            await RefreshOnceAsync(showSuccess);
        }
    }

    private async Task RefreshOnceAsync(bool showSuccess)
    {
        _loading = true;
        if (!_initialized)
        {
            SetStatus("Loading gateway configuration…");
        }

        try
        {
            var token = PageCancellationToken;
            var projectsTask = Configuration.GetBySpecAsync(
                new ProjectsOrderedByIdSpecification(),
                token);
            var serversTask = Configuration.GetBySpecAsync(
                new McpServersOrderedByIdSpecification(),
                token);
            var apiKeysTask = Configuration.GetBySpecAsync(
                new ApiKeysOrderedByIdSpecification(),
                token);
            await Task.WhenAll(projectsTask, serversTask, apiKeysTask);
            _projects = await projectsTask ?? [];
            _servers = await serversTask ?? [];
            _apiKeys = await apiKeysTask ?? [];

            await LoadCodexAsync(token);
            if (showSuccess && _codexError is null)
            {
                SetStatus("Up to date.", AdminStatusKind.Success);
            }
            else if (_codexError is not null)
            {
                SetStatus("Gateway configuration loaded, but the Codex account status is unavailable.", AdminStatusKind.Warning);
            }
            else if (!_initialized)
            {
                SetStatus(string.Empty);
            }

            _initialized = true;
            StartPollingIfNeeded();
        }
        catch (OperationCanceledException) when (PageCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while loading the gateway configuration.");
            }

            SetStatus(AdminText.Describe(exception), AdminStatusKind.Error);
        }
        finally
        {
            _loading = false;
        }
    }

    private async Task LoadCodexAsync(CancellationToken cancellationToken)
    {
        try
        {
            var codex = await Sender.Send(
                new GetCodexAuthenticationStateUseCase(),
                cancellationToken);
            _account = codex.Account;
            _login = codex.Login;
            _codexError = null;
        }
        catch (OperationCanceledException) when (PageCancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (GatewayException exception)
        {
            _account = null;
            _login = null;
            _codexError = AdminText.Describe(exception);
        }
        catch (Exception exception)
        {
            Logger.LogError(exception, "Unexpected failure while loading the Codex authentication state.");
            _account = null;
            _login = null;
            _codexError = AdminText.Describe(exception);
        }
    }

    private async Task HandleChangedAsync(string message)
    {
        await RefreshAsync(showSuccess: false);
        SetStatus(message, AdminStatusKind.Success);
    }

    private async Task HandleCodexChangedAsync(string message)
    {
        await RefreshAsync(showSuccess: false);
        SetStatus(message, _codexError is null ? AdminStatusKind.Success : AdminStatusKind.Error);
        StartPollingIfNeeded();
    }

    private async Task HandleCodexStatusActionAsync()
    {
        _selectedSection = AdminSection.Codex;
        if (_account?.Authenticated == true ||
            _login is { Status: DeviceLoginStatus.Pending } ||
            _codexError is not null)
        {
            return;
        }

        _codexActionBusy = true;
        try
        {
            await Sender.Send(new StartCodexDeviceLoginUseCase(), PageCancellationToken);
            await RefreshAsync(showSuccess: false);
            SetStatus("Codex device login started.", AdminStatusKind.Success);
            StartPollingIfNeeded();
        }
        catch (OperationCanceledException) when (PageCancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while starting Codex device login.");
            }

            SetStatus(AdminText.Describe(exception), AdminStatusKind.Error);
        }
        finally
        {
            _codexActionBusy = false;
        }
    }

    private void SelectSection(AdminSection section) => _selectedSection = section;

    private string NavButtonClass(AdminSection section) =>
        section == _selectedSection
            ? "nav-button nav-button-selected"
            : "nav-button";

    private void StartPollingIfNeeded()
    {
        if (_login is not { Status: DeviceLoginStatus.Pending } ||
            _pollTask is { IsCompleted: false })
        {
            return;
        }

        _pollTask = PollDeviceLoginAsync();
    }

    private async Task PollDeviceLoginAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (await timer.WaitForNextTickAsync(PageCancellationToken))
            {
                await InvokeAsync(async () =>
                {
                    await RefreshAsync(showSuccess: false);
                    StateHasChanged();
                });
                if (_login is not { Status: DeviceLoginStatus.Pending })
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (PageCancellationToken.IsCancellationRequested)
        {
        }
    }

    private enum AdminSection
    {
        Overview,
        ApiKeys,
        Projects,
        McpServers,
        Codex
    }

}
