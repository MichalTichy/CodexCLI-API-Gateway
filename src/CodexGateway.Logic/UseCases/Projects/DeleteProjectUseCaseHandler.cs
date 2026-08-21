using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.Projects;

public sealed class DeleteProjectUseCaseHandler(
    IGatewayConfigurationRepository repository,
    IProjectStorage projectStorage,
    RunCoordinator runs)
    : IRequestHandler<DeleteProjectUseCase>
{
    public async Task Handle(
        DeleteProjectUseCase request,
        CancellationToken cancellationToken)
    {
        if (runs.AdmittedRuns > 0)
        {
            throw new GatewayException(
                GatewayErrorCategory.Conflict,
                409,
                "runs_active",
                "Projects cannot be deleted while Codex runs are active or queued.");
        }

        await runs.ExecuteProjectOperationAsync(
            request.ProjectId,
            async token =>
            {
                string? canonicalId = null;
                await repository.UpdateAsync(state =>
                {
                    var project = new ProjectDefinitionByIdSpecification(request.ProjectId).Apply(state)
                        ?? throw GatewayException.NotFound(
                            $"Project '{request.ProjectId}' was not found.",
                            "project_not_found");
                    canonicalId = project.Id;
                    return state with
                    {
                        Projects = state.Projects
                            .Where(candidate => !string.Equals(
                                candidate.Id,
                                project.Id,
                                StringComparison.Ordinal))
                            .ToList()
                    };
                }, token);

                projectStorage.Delete(canonicalId!);
            },
            cancellationToken);
    }
}
