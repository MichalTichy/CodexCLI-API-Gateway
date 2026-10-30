using CodexGateway.Logic.UseCases.ApiKeys;
using CodexGateway.Logic.Security;
using CodexGateway.Models.ApiKeys;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CodexGateway.App.Components.Pages.Admin.ApiKeys;

public partial class ApiKeyList : AdminComponentBase
{
    private CreateApiKeyModel _create = new();
    private ApiKeyDefinition? _createdApiKey;
    private string? _deleteKeyId;
    private bool _deleteDialogOpen;
    private bool _revealCreatedSecret;
    private bool _secretCopied;

    private ApiKeyIdentity? DeleteTarget => ApiKeys.FirstOrDefault(key => key.Id == _deleteKeyId);

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ApiKeyList> Logger { get; set; } = null!;

    [Inject]
    private IJSRuntime JSRuntime { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ApiKeyIdentity> ApiKeys { get; set; } = [];

    [Parameter, EditorRequired]
    public IReadOnlyList<ProjectDefinition> Projects { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    [Parameter] public EventCallback<string> OnManageAccess { get; set; }

    private async Task CreateAsync()
    {
        ApiKeyDefinition? created = null;
        await RunAsync(
            async () => created = await Sender.Send(
                new CreateApiKeyUseCase(_create.Id, _create.Name),
                PageCancellationToken),
            async () =>
            {
                _createdApiKey = created;
                _revealCreatedSecret = false;
                _secretCopied = false;
                _create = new CreateApiKeyModel();
                await OnChanged.InvokeAsync($"API key {created!.Id} created. Copy its secret now.");
            },
            Logger,
            "Creating API key…",
            $"Unexpected failure while creating API key {_create.Id}.");
    }

    private void ToggleCreatedSecret() => _revealCreatedSecret = !_revealCreatedSecret;

    private async Task CopyCreatedSecretAsync()
    {
        if (_createdApiKey is null)
        {
            return;
        }

        await JSRuntime.InvokeVoidAsync("navigator.clipboard.writeText", _createdApiKey.Key);
        _secretCopied = true;
    }

    private void DismissCreatedSecret()
    {
        _createdApiKey = null;
        _revealCreatedSecret = false;
        _secretCopied = false;
    }

    private void AskToDelete(string id)
    {
        SetStatus(string.Empty);
        _deleteKeyId = id;
        _deleteDialogOpen = true;
    }

    private void CancelDelete()
    {
        _deleteDialogOpen = false;
        _deleteKeyId = null;
    }

    private async Task ConfirmDeleteAsync()
    {
        if (_deleteKeyId is null)
        {
            return;
        }

        var id = _deleteKeyId;
        await RunAsync(
            async () => await Sender.Send(new DeleteApiKeyUseCase(id), PageCancellationToken),
            async () =>
            {
                if (_createdApiKey?.Id == id)
                {
                    DismissCreatedSecret();
                }

                CancelDelete();
                await OnChanged.InvokeAsync(AdminUx.Format("ApiKeyDeleted", id));
            },
            Logger,
            AdminUx.Text("DeletingApiKey"),
            $"Unexpected failure while deleting API key {id}.");
    }

    private int ProjectGrantCount(string id) => Projects.Count(project =>
        project.ApiKeyAccess?.Any(access => access.ApiKeyId == id) == true);

    private string ProjectGrantSummary(string id)
    {
        var count = ProjectGrantCount(id);
        return count == 1 ? "1 project grant" : $"{count} project grants";
    }
}
