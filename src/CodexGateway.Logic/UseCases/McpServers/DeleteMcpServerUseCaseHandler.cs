using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed class DeleteMcpServerUseCaseHandler(IGatewayConfigurationRepository repository)
    : IRequestHandler<DeleteMcpServerUseCase>
{
    public async Task Handle(
        DeleteMcpServerUseCase request,
        CancellationToken cancellationToken)
    {
        await repository.UpdateAsync(state => state with
        {
            McpServers = state.McpServers
                .Where(server => server.Id != request.ServerId)
                .ToList(),
            Projects = state.Projects.Select(project => project with
            {
                ApiKeyAccess = project.ApiKeyAccess.Select(access => access with
                {
                    McpServers = access.McpServers
                        .Where(assignment => assignment.ServerId != request.ServerId)
                        .ToList()
                }).ToList()
            }).ToList()
        }, cancellationToken);
    }
}
