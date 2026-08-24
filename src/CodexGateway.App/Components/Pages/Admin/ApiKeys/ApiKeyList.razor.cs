using CodexGateway.Logic.UseCases.ApiKeys;
using CodexGateway.Logic.Security;
using CodexGateway.Models.ApiKeys;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.ApiKeys;

public partial class ApiKeyList : AdminComponentBase
{
    private CreateApiKeyModel _create = new();
    private ApiKeyDefinition? _createdApiKey;

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ApiKeyList> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<ApiKeyIdentity> ApiKeys { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

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
                _create = new CreateApiKeyModel();
                await OnChanged.InvokeAsync($"API key {created!.Id} created. Copy its secret now.");
            },
            Logger,
            "Creating API key…",
            $"Unexpected failure while creating API key {_create.Id}.");
    }

    private async Task DeleteAsync(string id) =>
        await RunAsync(
            async () => await Sender.Send(new DeleteApiKeyUseCase(id), PageCancellationToken),
            () => OnChanged.InvokeAsync($"API key {id} revoked."),
            Logger,
            "Revoking API key…",
            $"Unexpected failure while revoking API key {id}.");
}
