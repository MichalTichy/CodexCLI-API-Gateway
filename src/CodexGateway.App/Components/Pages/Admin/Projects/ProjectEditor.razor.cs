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

public partial class ProjectEditor : ComponentBase
{
    private ProjectDefinition? _source;
    private ProjectEditorModel _editor = new() { Id = string.Empty };
    private bool _busy;
    private bool _confirmDelete;
    private string _status = string.Empty;
    private string _statusKind = string.Empty;
    private string _savedFingerprint = string.Empty;

    private bool IsDirty => Fingerprint(_editor) != _savedFingerprint;

    private string SaveState => _busy ? "Saving…" : IsDirty ? "Unsaved changes" : "Saved";

    private string SaveStateClass => _busy ? "save-state-busy" : IsDirty ? "save-state-dirty" : "save-state-saved";

    private int GrantedApiKeyCount => _editor.ApiKeys.Count(key => key.HasProjectAccess);

    private int GrantedServerCount => _editor.ApiKeys
        .Where(key => key.HasProjectAccess)
        .Sum(key => key.Servers.Count(server => server.Granted));

    private int EnabledToolCount => _editor.ApiKeys
        .Where(key => key.HasProjectAccess)
        .SelectMany(key => key.Servers.Where(server => server.Granted))
        .Sum(server => server.Tools.Count(tool => tool.Enabled));

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

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_source, Project))
        {
            _source = Project;
            _editor = ProjectEditorModel.From(Project, ApiKeys, Servers);
            _savedFingerprint = Fingerprint(_editor);
            _status = string.Empty;
            _statusKind = string.Empty;
            _confirmDelete = false;
        }
    }

    private async Task SaveAsync()
    {
        if (_busy)
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

    private static void SetAllTools(McpGrantEditorModel server, bool enabled)
    {
        foreach (var tool in server.Tools)
        {
            tool.Enabled = enabled;
        }
    }

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
                CancellationToken.None);
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
            value.Append('\u001e').Append(key.Id).Append('\u001f').Append(key.HasProjectAccess);
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
