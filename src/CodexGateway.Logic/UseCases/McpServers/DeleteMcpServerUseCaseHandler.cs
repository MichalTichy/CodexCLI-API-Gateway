using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed class DeleteMcpServerUseCaseHandler(IRepository<GatewayState> repository)
    : IRequestHandler<DeleteMcpServerUseCase>
{
    public async Task Handle(
        DeleteMcpServerUseCase request,
        CancellationToken cancellationToken)
    {
        await repository.GetAndUpdateAsync(GatewayState.DocumentId, state =>
        {
            state.McpServers = state.McpServers
                .Where(server => server.Id != request.ServerId)
                .ToList();
            state.Projects = state.Projects.Select(project => project with
            {
                ApiKeyAccess = project.ApiKeyAccess.Select(access => access with
                {
                    McpServers = access.McpServers
                        .Where(assignment => assignment.ServerId != request.ServerId)
                        .ToList()
                }).ToList()
            }).ToList();
        }, cancellationToken);
    }
}
