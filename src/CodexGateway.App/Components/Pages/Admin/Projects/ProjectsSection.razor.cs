using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Admin;

public partial class ProjectsSection : ComponentBase
{
    private CreateProjectModel _create = new();
    private bool _busy;
    private string _status = string.Empty;
    private string _statusKind = string.Empty;

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ProjectsSection> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ProjectDefinition> Projects { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<GlobalApiKeyIdentity> ApiKeys { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<McpServerDefinition> Servers { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    private async Task CreateAsync()
    {
        if (_busy)
        {
            return;
        }

        _busy = true;
        _status = "Creating project…";
        _statusKind = string.Empty;
        try
        {
            var project = await Sender.Send(new CreateProjectUseCase(_create.Id, _create.Name));
            _create = new CreateProjectModel();
            _status = string.Empty;
            await OnChanged.InvokeAsync($"Project {project.Id} created.");
        }
        catch (Exception exception)
        {
            if (exception is not GatewayException)
            {
                Logger.LogError(exception, "Unexpected failure while creating project {ProjectId}.", _create.Id);
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
