using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications.Projects;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models.Gateway;
using CodexGateway.Models.Projects;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed class EnsureSetupProjectUseCaseHandler(IReadOnlyRepository<GatewayState> repository, ISender sender)
    : IRequestHandler<EnsureSetupProjectUseCase, ProjectDefinition>
{
    public async Task<ProjectDefinition> Handle(EnsureSetupProjectUseCase request, CancellationToken cancellationToken)
    {
        var id = request.Id?.Trim().ToLowerInvariant() ?? string.Empty;
        var projects = await repository.GetBySpecAsync(new ProjectsOrderedByIdSpecification(), cancellationToken) ?? [];
        var existing = projects.SingleOrDefault(project => project.Id == id);
        if (existing is not null)
        {
            if (existing.Name != request.Name?.Trim())
            {
                throw new GatewayException(409, "setup_collision", "A project with this ID already exists under a different name. Choose another ID; existing settings were preserved.");
            }

            return existing;
        }

        return await sender.Send(new CreateProjectUseCase(id, request.Name!), cancellationToken);
    }
}
