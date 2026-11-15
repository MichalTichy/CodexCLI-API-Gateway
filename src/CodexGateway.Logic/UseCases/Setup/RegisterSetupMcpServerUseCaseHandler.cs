using System.Text.Json;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications.McpServers;
using CodexGateway.Logic.UseCases.McpServers;
using CodexGateway.Models.Gateway;
using CodexGateway.Models.McpServers;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed class RegisterSetupMcpServerUseCaseHandler(IReadOnlyRepository<GatewayState> repository, ISender sender)
    : IRequestHandler<RegisterSetupMcpServerUseCase, McpServerDefinition>
{
    public async Task<McpServerDefinition> Handle(RegisterSetupMcpServerUseCase request, CancellationToken cancellationToken)
    {
        var normalized = request.Server with
        {
            Id = request.Server.Id?.Trim().ToLowerInvariant() ?? string.Empty,
            Name = request.Server.Name?.Trim() ?? string.Empty,
            AvailableTools = (request.Server.AvailableTools ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(),
            EnvironmentVariables = (request.Server.EnvironmentVariables ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList()
        };
        var servers = await repository.GetBySpecAsync(new McpServersOrderedByIdSpecification(), cancellationToken) ?? [];
        var existing = servers.SingleOrDefault(server => server.Id == normalized.Id);
        if (existing is not null && !request.Replace)
        {
            if (!JsonElement.DeepEquals(JsonSerializer.SerializeToElement(existing), JsonSerializer.SerializeToElement(normalized)))
            {
                throw new GatewayException(409, "setup_collision", "This MCP server already exists with different settings. Read and review them before explicitly replacing it.");
            }

            return existing;
        }

        return await sender.Send(new CreateOrUpdateMcpServerUseCase(normalized), cancellationToken);
    }
}
