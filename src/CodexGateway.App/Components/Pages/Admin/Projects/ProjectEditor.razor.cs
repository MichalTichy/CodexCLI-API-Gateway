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

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ProjectEditor> Logger { get; set; } = null!;

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
            _status = string.Empty;
            await OnChanged.InvokeAsync($"Saved project {Project.Id}.");
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
