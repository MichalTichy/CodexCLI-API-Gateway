using CodexGateway.Logic.UseCases.ApiKeys;
using CodexGateway.Logic.Security;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Admin;

public partial class ApiKeyList : AdminComponentBase
{
    private CreateApiKeyModel _create = new();

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<ApiKeyList> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<GlobalApiKeyIdentity> ApiKeys { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    private async Task CreateAsync()
    {
        string? createdId = null;
        await RunAsync(
            async () => createdId = (await Sender.Send(
                new CreateApiKeyUseCase(_create.Id, _create.Name, _create.Key),
                PageCancellationToken)).Id,
            async () =>
            {
                _create = new CreateApiKeyModel();
                await OnChanged.InvokeAsync($"API key {createdId} created.");
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
