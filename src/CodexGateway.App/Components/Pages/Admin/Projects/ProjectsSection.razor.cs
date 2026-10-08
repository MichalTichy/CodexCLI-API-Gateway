using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.Projects;

public partial class ProjectsSection : ComponentBase
{
    private CreateProjectModel _create = new();
    private bool _busy;
    private string _status = string.Empty;
    private string _statusKind = string.Empty;
    private string? _selectedProjectId;
    private string _search = string.Empty;
    private readonly HashSet<string> _openedProjects = [];
    private int _lastRequestVersion = -1;

    private IReadOnlyList<ProjectDefinition> FilteredProjects => Projects.Where(p =>
        string.IsNullOrWhiteSpace(_search) || p.Name.Contains(_search, StringComparison.OrdinalIgnoreCase) ||
        p.Id.Contains(_search, StringComparison.OrdinalIgnoreCase)).ToArray();

    [Parameter] public string? RequestedApiKeyId { get; set; }
    [Parameter] public int AccessRequestVersion { get; set; }

    protected override void OnParametersSet()
    {
        if (RequestedApiKeyId is not null && AccessRequestVersion != _lastRequestVersion)
        {
            _lastRequestVersion = AccessRequestVersion;
            OpenProject(Projects.FirstOrDefault(p => p.ApiKeyAccess.Any(a => a.ApiKeyId == RequestedApiKeyId))?.Id ?? Projects.FirstOrDefault()?.Id);
        }
    }

    private void OpenProject(string? id)
    {
        _selectedProjectId = id;
        if (id is not null) { _openedProjects.Add(id); }
    }

    private static string ProjectSummary(ProjectDefinition project) => AdminUx.Format("ListProjectCounts",
        project.ApiKeyAccess.Count, project.ApiKeyAccess.SelectMany(k => k.McpServers).Select(s => s.ServerId).Distinct().Count());

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ProjectsSection> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ProjectDefinition> Projects { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<ApiKeyIdentity> ApiKeys { get; set; } = [];

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
