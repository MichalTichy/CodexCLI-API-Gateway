using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications.Projects;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models.Gateway;
using CodexGateway.Models.Projects;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed class SetSetupProjectAccessUseCaseHandler(IReadOnlyRepository<GatewayState> repository, ISender sender)
    : IRequestHandler<SetSetupProjectAccessUseCase, ProjectDefinition>
{
    public async Task<ProjectDefinition> Handle(SetSetupProjectAccessUseCase request, CancellationToken cancellationToken)
    {
        var projects = await repository.GetBySpecAsync(new ProjectsOrderedByIdSpecification(), cancellationToken) ?? [];
        var project = projects.SingleOrDefault(project => project.Id == request.ProjectId)
            ?? throw GatewayException.NotFound("Project was not found.", "project_not_found");
        return await sender.Send(new UpdateProjectUseCase(project with
        {
            ApiKeyAccess = [.. project.ApiKeyAccess.Where(access => access.ApiKeyId != request.Access.ApiKeyId), request.Access]
        }), cancellationToken);
    }
}
