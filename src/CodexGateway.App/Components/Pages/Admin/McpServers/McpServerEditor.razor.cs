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

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<McpServerEditor> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public McpServerDefinition Server { get; set; } = null!;

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_source, Server))
        {
            _source = Server;
            _editor = McpServerEditorModel.From(Server);
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
            await Sender.Send(new CreateOrUpdateMcpServerUseCase(_editor.ToDefinition()));
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
