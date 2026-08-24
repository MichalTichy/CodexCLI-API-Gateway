using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Models;
using MediatR;
using Microsoft.AspNetCore.Components;

namespace CodexGateway.App.Components.Pages.Admin.McpServers;

public partial class McpCatalogSection : AdminComponentBase
{
    private McpServerEditorModel _create = new()
    {
        Enabled = true,
        ExecutionMode = McpExecutionMode.Gateway
    };

    [Inject]
    private ISender Sender { get; set; } = null!;

    [Inject]
    private ILogger<McpCatalogSection> Logger { get; set; } = null!;

    [Parameter, EditorRequired]
    public IReadOnlyList<McpServerDefinition> Servers { get; set; } = [];

    [Parameter, EditorRequired]
    public EventCallback<string> OnChanged { get; set; }

    private async Task CreateAsync()
    {
        string? createdId = null;
        await RunAsync(
            async () => createdId = (await Sender.Send(
                new CreateOrUpdateMcpServerUseCase(_create.ToDefinition()),
                PageCancellationToken)).Id,
            async () =>
            {
                _create = new McpServerEditorModel
                {
                    Enabled = true,
                    ExecutionMode = McpExecutionMode.Gateway
                };
                await OnChanged.InvokeAsync($"Trusted MCP server {createdId} added.");
            },
            Logger,
            "Adding trusted MCP server…",
            $"Unexpected failure while creating MCP server {_create.Id}.");
    }
}
