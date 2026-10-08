using System.Text.Json;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Codex.Models;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.McpServers;

public partial class McpServerEditor : ComponentBase
{
    private McpServerDefinition? _source;
    private McpServerEditorModel _editor = new();
    private bool _busy;
    private bool _confirmDelete;
    private string _status = string.Empty;
    private string _statusKind = string.Empty;
    private IReadOnlyList<McpToolMetadata> _discoveredTools = [];
    private bool _testingConnection;
    private bool _connectionVerified;
    private bool _confirmDiscard;

    private bool HasExternalChanges => IsDirty && !ReferenceEquals(_source, Server) &&
        Fingerprint(McpServerEditorModel.From(Server)) != _savedFingerprint;

    private void AskDiscard() => _confirmDiscard = true;
    private void CancelDiscard() => _confirmDiscard = false;
    private void DiscardChanges()
    {
        _source = null;
        _confirmDiscard = false;
        OnParametersSet();
    }
    private string _savedFingerprint = string.Empty;
    private string _savedConnectionFingerprint = string.Empty;
    private string? _testedConnectionFingerprint;

    private bool ConnectionVerified => _connectionVerified && string.Equals(
        _testedConnectionFingerprint,
        ConnectionFingerprint(_editor),
        StringComparison.Ordinal);

    private string _toolSearch = string.Empty;
    private string _toolRiskFilter = "all";
    private string _toolSelectionFilter = "all";
    private string? _undoAllowlist;

    private IReadOnlyList<McpToolMetadata> FilteredDiscoveredTools => _discoveredTools.Where(tool =>
        (string.IsNullOrWhiteSpace(_toolSearch) || tool.Name.Contains(_toolSearch, StringComparison.OrdinalIgnoreCase) ||
            tool.Description?.Contains(_toolSearch, StringComparison.OrdinalIgnoreCase) == true) &&
        (_toolRiskFilter == "all" || ToolRiskPresentation.Classify(tool.Annotations).ToString() == _toolRiskFilter) &&
        (_toolSelectionFilter == "all" || IsToolSelected(tool.Name) == (_toolSelectionFilter == "selected"))).ToArray();

    private void UndoDiscoveredToolChanges()
    {
        if (_undoAllowlist is not null) { _editor.AvailableTools = _undoAllowlist; _undoAllowlist = null; }
    }

    private int SelectedDiscoveredToolCount => _discoveredTools.Count(tool => IsToolSelected(tool.Name));

    private bool ConnectionSettingsChanged => !string.Equals(
        _savedConnectionFingerprint,
        ConnectionFingerprint(_editor),
        StringComparison.Ordinal);

    private bool IsDirty => !string.Equals(
        _savedFingerprint,
        Fingerprint(_editor),
        StringComparison.Ordinal);

    private string TransportHelp => _editor.IsHttp
        ? "Connects to a remote MCP endpoint."
        : "Launches an executable and communicates over standard input/output.";

    private string ExecutionHelp => _editor.ExecutionMode == McpExecutionMode.Gateway
        ? "The gateway process owns this connection."
        : "Advanced: every isolated run container owns its own connection.";

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<McpServerEditor> Logger { get; set; } = null!;

    [Inject]
    private IMcpMetadataDiscoveryService McpMetadataDiscovery { get; set; } = null!;

    [Parameter, EditorRequired]
    public McpServerDefinition Server { get; set; } = null!;

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    protected override void OnParametersSet()
    {
        if (_source is null || (!ReferenceEquals(_source, Server) && !IsDirty))
        {
            _source = Server;
            _editor = McpServerEditorModel.From(Server);
            _status = string.Empty;
            _statusKind = string.Empty;
            _confirmDelete = false;
            _discoveredTools = [];
            _undoAllowlist = null;
            _connectionVerified = false;
            _savedFingerprint = Fingerprint(_editor);
            _savedConnectionFingerprint = ConnectionFingerprint(_editor);
            _testedConnectionFingerprint = null;
        }
    }

    private async Task SaveAsync()
    {
        if (_busy || HasExternalChanges || (ConnectionSettingsChanged && !ConnectionVerified))
        {
            return;
        }

        _busy = true;
        _status = "Saving…";
        _statusKind = string.Empty;
        try
        {
            await Sender.Send(new CreateOrUpdateMcpServerUseCase(_editor.ToDefinition()));
            _savedFingerprint = Fingerprint(_editor);
            _savedConnectionFingerprint = ConnectionFingerprint(_editor);
            _status = string.Empty;
            await OnChanged.InvokeAsync($"Saved MCP server {Server.Id}.");
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while updating MCP server {McpServerId}.", Server.Id);
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

    private async Task TestConnectionAsync()
    {
        if (_testingConnection)
        {
            return;
        }

        _testingConnection = true;
        _connectionVerified = false;
        _status = "Testing connection and discovering tools…";
        _statusKind = string.Empty;
        try
        {
            var definition = _editor.ToDefinition();
            var discovered = await McpMetadataDiscovery.DiscoverAsync(
                [new ResolvedMcpServer(definition, [], Required: false)],
                CancellationToken.None);
            var server = discovered.SingleOrDefault(item => string.Equals(item.ServerId, definition.Id, StringComparison.Ordinal));
            if (server?.ServerInfo is null)
            {
                throw GatewayException.InvalidRequest("The MCP server did not return valid server information.");
            }

            _discoveredTools = server.Tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
            _connectionVerified = true;
            _testedConnectionFingerprint = ConnectionFingerprint(_editor);
            _status = $"Connection verified. {Pluralize(_discoveredTools.Count, "tool")} discovered.";
            _statusKind = "success";
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while testing MCP server {McpServerId}.", Server.Id);
            }

            _discoveredTools = [];
            _status = exception is GatewayException
                ? AdminText.Describe(exception)
                : "Unable to connect and read MCP metadata. Check the endpoint or executable, runtime environment, and gateway logs.";
            _statusKind = "error";
        }
        finally
        {
            _testingConnection = false;
        }
    }

    private bool IsToolSelected(string toolName) =>
        AdminText.Lines(_editor.AvailableTools).Contains(toolName, StringComparer.Ordinal);

    private void SetAllDiscoveredTools(bool selected)
    {
        _undoAllowlist = _editor.AvailableTools;
        var allowlist = AdminText.Lines(_editor.AvailableTools).ToHashSet(StringComparer.Ordinal);
        foreach (var tool in FilteredDiscoveredTools)
        {
            if (selected) { allowlist.Add(tool.Name); } else { allowlist.Remove(tool.Name); }
        }
        _editor.AvailableTools = string.Join(Environment.NewLine, allowlist.OrderBy(name => name, StringComparer.Ordinal));
    }

    private void ToggleDiscoveredTool(string toolName, ChangeEventArgs args)
    {
        var selected = AdminText.Lines(_editor.AvailableTools).ToHashSet(StringComparer.Ordinal);
        if (args.Value is true)
        {
            selected.Add(toolName);
        }
        else
        {
            selected.Remove(toolName);
        }

        _editor.AvailableTools = string.Join(Environment.NewLine, selected.OrderBy(name => name, StringComparer.Ordinal));
    }

    private static string FormatJson(JsonElement value)
    {
        using var document = JsonDocument.Parse(value.GetRawText());
        return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string Pluralize(int count, string noun) => $"{count} {noun}{(count == 1 ? string.Empty : "s")}";

    private static string ConnectionFingerprint(McpServerEditorModel model) => string.Join(
        '\u001f',
        model.IsHttp,
        model.ExecutionMode,
        model.Location,
        model.Arguments,
        model.EnvironmentHeaders,
        model.EnvironmentVariables);

    private static string Fingerprint(McpServerEditorModel model) => string.Join(
        '\u001f',
        model.Name,
        model.Enabled,
        ConnectionFingerprint(model),
        model.AvailableTools);

    private async Task DeleteAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _status = "Deleting…";
        _statusKind = string.Empty;
        try
        {
            await Sender.Send(new DeleteMcpServerUseCase(Server.Id));
            await OnChanged.InvokeAsync($"Deleted MCP server {Server.Id} and removed its project grants.");
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while deleting MCP server {McpServerId}.", Server.Id);
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
