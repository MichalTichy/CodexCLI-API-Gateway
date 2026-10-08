using System.Text.Json;
using System.Text;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.Projects;

public partial class ProjectEditor : ComponentBase, IDisposable
{
    private ProjectDefinition? _source;
    private ProjectEditorModel _editor = new() { Id = string.Empty };
    private bool _busy;
    private bool _confirmDelete;
    private string _status = string.Empty;
    private string _statusKind = string.Empty;
    private string _savedFingerprint = string.Empty;
    private string _tab = "access";
    private string? _selectedKeyId;
    private string? _selectedServerId;
    private string _toolSearch = string.Empty;
    private string _accessFilter = "all";
    private string _riskFilter = "all";
    private string _compareKeyId = string.Empty;
    private bool _differencesOnly;
    private bool _confirmDiscard;
    private Dictionary<ToolGrantEditorModel, bool>? _undoTools;
    private ProjectEditorModel _savedEditor = new() { Id = string.Empty };
    private readonly CancellationTokenSource _metadataLifetime = new();
    private bool _disposed;

    private ApiKeyAccessEditorModel? SelectedKey => _editor.ApiKeys.FirstOrDefault(k => k.Id == _selectedKeyId);
    private McpGrantEditorModel? SelectedServer => SelectedKey?.Servers.FirstOrDefault(s => s.Id == _selectedServerId);
    private ApiKeyAccessEditorModel? ComparisonKey => _editor.ApiKeys.FirstOrDefault(k => k.Id == _compareKeyId && k.Id != _selectedKeyId);
    private string SelectedContext => _tab == "access" ? $" / {SelectedKey?.Name} / {SelectedServer?.Name}" : string.Empty;
    private bool HasExternalChanges => IsDirty && !ReferenceEquals(_source, Project) &&
        Fingerprint(ProjectEditorModel.From(Project, ApiKeys, Servers)) != _savedFingerprint;

    private IReadOnlyList<ToolGrantEditorModel> FilteredTools => SelectedServer?.Tools.Where(tool =>
        (string.IsNullOrWhiteSpace(_toolSearch) || tool.Name.Contains(_toolSearch, StringComparison.OrdinalIgnoreCase) ||
            tool.Description?.Contains(_toolSearch, StringComparison.OrdinalIgnoreCase) == true) &&
        (_accessFilter == "all" || tool.Enabled == (_accessFilter == "allowed")) &&
        (_riskFilter == "all" || tool.Risk.ToString() == _riskFilter) &&
        (!_differencesOnly || ComparisonKey is null || CurrentAllows(tool) != ComparisonAllows(tool.Name))).ToArray() ?? [];

    private bool CurrentAllows(ToolGrantEditorModel tool) => SelectedKey is { HasProjectAccess: true } &&
        SelectedServer is { Granted: true, CatalogEnabled: true } && tool.Enabled;

    private bool ComparisonAllows(string name) => ComparisonKey is { HasProjectAccess: true } key &&
        key.Servers.Any(s => s.Id == _selectedServerId && s.Granted && s.CatalogEnabled && s.Tools.Any(t => t.Name == name && t.Enabled));

    private static string GrantSummary(ApiKeyAccessEditorModel key, McpGrantEditorModel grant) =>
        !key.HasProjectAccess || !grant.Granted || !grant.CatalogEnabled ? AdminUx.Text("NoAccess") :
            AdminUx.Format("MatrixTools", grant.Tools.Count(t => t.Enabled), grant.Tools.Count);

    private void SelectKey(string id)
    {
        _selectedKeyId = id;
        _selectedServerId ??= SelectedKey?.Servers.FirstOrDefault()?.Id;
        ResetToolFilters();
    }

    private void SelectGrant(string keyId, string serverId)
    {
        _selectedKeyId = keyId;
        _selectedServerId = serverId;
        ResetToolFilters();
    }

    private void SelectServer(ChangeEventArgs args)
    {
        _selectedServerId = args.Value?.ToString();
        ResetToolFilters();
    }

    private void ResetToolFilters()
    {
        _toolSearch = string.Empty;
        _accessFilter = _riskFilter = "all";
        _undoTools = null;
    }

    private void SetFilteredTools(bool enabled)
    {
        var tools = FilteredTools;
        _undoTools = tools.ToDictionary(t => t, t => t.Enabled);
        foreach (var tool in tools) { tool.Enabled = enabled; }
    }

    private void UndoToolChanges()
    {
        if (_undoTools is null) { return; }
        foreach (var (tool, enabled) in _undoTools) { tool.Enabled = enabled; }
        _undoTools = null;
    }

    private void DiscardChanges() => _confirmDiscard = true;
    private void CancelDiscard() => _confirmDiscard = false;
    private void ConfirmDiscard()
    {
        _source = null;
        _savedFingerprint = string.Empty;
        _editor = new() { Id = string.Empty };
        _confirmDiscard = false;
        OnParametersSet();
    }

    private IReadOnlyList<string> PendingChanges
    {
        get
        {
            var changes = new List<string>();
            if (_editor.Name != _savedEditor.Name) { changes.Add(AdminUx.Format("NameChanged", _savedEditor.Name, _editor.Name)); }
            if (_editor.Enabled != _savedEditor.Enabled) { changes.Add(AdminUx.Format("RequestsChanged", _editor.Enabled)); }
            if (_editor.RunnerImage != _savedEditor.RunnerImage) { changes.Add(AdminUx.Text("RunnerChanged")); }
            foreach (var key in _editor.ApiKeys)
            {
                var savedKey = _savedEditor.ApiKeys.FirstOrDefault(k => k.Id == key.Id);
                if (savedKey is null) { continue; }
                if (key.HasProjectAccess != savedKey.HasProjectAccess) { changes.Add(AdminUx.Format("KeyChanged", key.Name, key.HasProjectAccess ? AdminUx.Text("Allowed") : AdminUx.Text("Blocked"))); }
                if (key.WebSearchMode != savedKey.WebSearchMode) { changes.Add($"{key.Name}: Codex web search → {key.WebSearchMode}"); }
                foreach (var server in key.Servers)
                {
                    var savedServer = savedKey.Servers.FirstOrDefault(s => s.Id == server.Id);
                    if (savedServer is null) { continue; }
                    if (server.Granted != savedServer.Granted || server.Required != savedServer.Required) { changes.Add(AdminUx.Format("ServerChanged", key.Name, server.Name, server.Granted ? AdminUx.Text("Allowed") : AdminUx.Text("Blocked"), server.Required)); }
                    foreach (var tool in server.Tools.Where(t => t.Enabled != savedServer.Tools.FirstOrDefault(s => s.Name == t.Name)?.Enabled))
                    {
                        changes.Add($"{key.Name} / {server.Name} / {tool.Name}: {(tool.Enabled ? AdminUx.Text("Allowed") : AdminUx.Text("Blocked"))} · {ToolRiskPresentation.Label(tool.Risk)}");
                    }
                }
            }
            return changes;
        }
    }

    private bool IsDirty => Fingerprint(_editor) != _savedFingerprint;

    private string SaveState => _busy ? "Saving…" : IsDirty ? "Unsaved changes" : "Saved";

    private string SaveStateClass => _busy ? "save-state-busy" : IsDirty ? "save-state-dirty" : "save-state-saved";

    private int GrantedApiKeyCount => _editor.ApiKeys.Count(key => key.HasProjectAccess);

    private int GrantedServerCount => _editor.ApiKeys
        .Where(key => key.HasProjectAccess)
        .SelectMany(key => key.Servers.Where(server => server.Granted && server.CatalogEnabled))
        .Select(server => server.Id).Distinct(StringComparer.Ordinal).Count();

    private int EnabledToolCount => _editor.ApiKeys
        .Where(key => key.HasProjectAccess)
        .Sum(key =>
            key.Servers
                .Where(server => server.Granted)
                .Sum(server => server.Tools.Count(tool => tool.Enabled)) +
            (key.WebSearchMode == WebSearchMode.Disabled ? 0 : 1));

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ProjectEditor> Logger { get; set; } = null!;

    [Inject]
    private IMcpMetadataDiscoveryService McpMetadataDiscovery { get; set; } = null!;

    [Parameter, EditorRequired]
    public ProjectDefinition Project { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ApiKeyIdentity> ApiKeys { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<McpServerDefinition> Servers { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    [Parameter] public string? FocusedApiKeyId { get; set; }
    [Parameter] public int FocusRequestVersion { get; set; }
    private int _lastFocusRequestVersion = -1;

    protected override void OnParametersSet()
    {
        if (FocusedApiKeyId is not null && FocusRequestVersion != _lastFocusRequestVersion)
        {
            _lastFocusRequestVersion = FocusRequestVersion;
            _selectedKeyId = FocusedApiKeyId;
            _tab = "access";
        }
        if (_source is null || (!ReferenceEquals(_source, Project) && !IsDirty))
        {
            _source = Project;
            _editor = ProjectEditorModel.From(Project, ApiKeys, Servers);
            _savedFingerprint = Fingerprint(_editor);
            _savedEditor = ProjectEditorModel.From(Project, ApiKeys, Servers);
            _selectedKeyId ??= _editor.ApiKeys.FirstOrDefault()?.Id;
            _selectedServerId ??= SelectedKey?.Servers.FirstOrDefault()?.Id;
            _undoTools = null;
            _status = string.Empty;
            _statusKind = string.Empty;
            _confirmDelete = false;
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_disposed && SelectedServer is { CatalogEnabled: true, MetadataLoading: false, MetadataAttemptedAt: null } server)
        {
            await DiscoverToolDetailsAsync(server.Id);
            if (!_disposed) { StateHasChanged(); }
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _metadataLifetime.Cancel();
        _metadataLifetime.Dispose();
    }

    private async Task SaveAsync()
    {
        if (_busy || HasExternalChanges)
        {
            return;
        }

        _busy = true;
        _status = "Saving…";
        _statusKind = string.Empty;
        try
        {
            await Sender.Send(new UpdateProjectUseCase(_editor.ToDefinition()));
            _savedFingerprint = Fingerprint(_editor);
            _savedEditor = ProjectEditorModel.From(_editor.ToDefinition(), ApiKeys, Servers);
            _undoTools = null;
            _status = string.Empty;
            await OnChanged.InvokeAsync($"Saved changes to “{_editor.Name}”.");
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while updating project {ProjectId}.", Project.Id);
            }

            _status = AdminText.Describe(exception);
            _statusKind = "error";
        }
        finally
        {
            _busy = false;
        }
    }

    private void ConfirmDelete() => _confirmDelete = true;

    private void CancelDelete() => _confirmDelete = false;

    private async Task DiscoverToolDetailsAsync(string serverId)
    {
        var matchingGrants = _editor.ApiKeys
            .SelectMany(key => key.Servers)
            .Where(server => string.Equals(server.Id, serverId, StringComparison.Ordinal))
            .ToArray();
        var definition = Servers.Single(server => string.Equals(server.Id, serverId, StringComparison.Ordinal));
        foreach (var grant in matchingGrants)
        {
            grant.MetadataLoading = true;
            grant.MetadataError = null;
            grant.MetadataAttemptedAt = DateTimeOffset.Now;
        }

        try
        {
            var discovered = await McpMetadataDiscovery.DiscoverAsync(
                [new ResolvedMcpServer(definition, definition.AvailableTools, Required: false)],
                _metadataLifetime.Token);
            var metadata = discovered.SingleOrDefault(server => string.Equals(server.ServerId, serverId, StringComparison.Ordinal));
            if (metadata?.ServerInfo is null)
            {
                throw GatewayException.InvalidRequest(
                    "The MCP server did not return valid server or tool information. Check its connection settings and try again.");
            }

            foreach (var grant in matchingGrants)
            {
                foreach (var tool in grant.Tools)
                {
                    var details = metadata?.Tools.SingleOrDefault(item => string.Equals(item.Name, tool.Name, StringComparison.Ordinal));
                    if (details is null)
                    {
                        tool.AvailableInDiscovery = false;
                        continue;
                    }

                    var inputSchema = FormatJson(details.InputSchema);
                    tool.SchemaChanged = tool.InputSchema is not null &&
                                         !string.Equals(tool.InputSchema, inputSchema, StringComparison.Ordinal);
                    tool.AvailableInDiscovery = true;
                    tool.Description = details.Description;
                    tool.Annotations = details.Annotations;
                    tool.InputSchema = inputSchema;
                }

                grant.MetadataLoaded = true;
                grant.MetadataDiscoveredAt = DateTimeOffset.Now;
                var unavailableCount = grant.Tools.Count(tool => tool.AvailableInDiscovery == false);
                var changedCount = grant.Tools.Count(tool => tool.SchemaChanged);
                grant.MetadataWarning = unavailableCount > 0
                    ? $"{Pluralize(unavailableCount, "stored tool grant")} not returned by the latest discovery. The grant is preserved but unavailable until the server exposes it again."
                    : changedCount > 0
                        ? $"{Pluralize(changedCount, "tool schema")} changed. Review the new input schema before continuing to grant access."
                        : null;
            }
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while discovering tools for MCP server {ServerId}.", serverId);
            }

            foreach (var grant in matchingGrants)
            {
                grant.MetadataError = exception is GatewayException
                    ? AdminText.Describe(exception)
                    : "Unable to connect and read tool details. Check the MCP server connection settings and gateway logs.";
            }
        }
        finally
        {
            foreach (var grant in matchingGrants)
            {
                grant.MetadataLoading = false;
            }
        }
    }

    private static string FormatJson(JsonElement value)
    {
        using var document = JsonDocument.Parse(value.GetRawText());
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Fingerprint(ProjectEditorModel editor)
    {
        var value = new StringBuilder()
            .Append(editor.Id).Append('\u001f')
            .Append(editor.Name).Append('\u001f')
            .Append(editor.Enabled).Append('\u001f')
            .Append(editor.RunnerImage);
        foreach (var key in editor.ApiKeys)
        {
            value.Append('\u001e').Append(key.Id).Append('\u001f')
                .Append(key.HasProjectAccess).Append('\u001f').Append(key.WebSearchMode);
            foreach (var server in key.Servers)
            {
                value.Append('\u001d').Append(server.Id).Append('\u001f')
                    .Append(server.Granted).Append('\u001f').Append(server.Required);
                foreach (var tool in server.Tools)
                {
                    value.Append('\u001c').Append(tool.Name).Append('\u001f').Append(tool.Enabled);
                }
            }
        }

        return value.ToString();
    }

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private async Task DeleteAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _status = "Deleting project and stored artifacts…";
        _statusKind = string.Empty;
        try
        {
            await Sender.Send(new DeleteProjectUseCase(Project.Id));
            await OnChanged.InvokeAsync($"Deleted project {Project.Id} and its stored artifacts.");
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while deleting project {ProjectId}.", Project.Id);
            }

            _status = AdminText.Describe(exception);
            _statusKind = "error";
        }
        finally
        {
            _busy = false;
        }
    }
}
