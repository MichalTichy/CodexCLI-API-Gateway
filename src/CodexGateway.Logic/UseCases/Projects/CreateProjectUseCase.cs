using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;
using System.Text.RegularExpressions;

namespace CodexGateway.Logic.UseCases.Projects;

public sealed record CreateProjectUseCase(string Id, string Name) : IRequest<ProjectDefinition>;

public sealed partial class CreateProjectUseCaseHandler(
    IRepository<GatewayState> repository,
    IProjectStorage projectStorage)
    : IRequestHandler<CreateProjectUseCase, ProjectDefinition>
{
    public async Task<ProjectDefinition> Handle(
        CreateProjectUseCase request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            throw GatewayException.InvalidRequest("Project ID is required.", parameter: "id");
        }

        var id = request.Id.Trim().ToLowerInvariant();
        if (!ProjectIdPattern().IsMatch(id))
        {
            throw GatewayException.InvalidRequest(
                "Project IDs must contain 2-64 lowercase letters, numbers, or hyphens.",
                parameter: "id");
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw GatewayException.InvalidRequest("Project name is required.", parameter: "name");
        }

        var name = request.Name.Trim();
        ProjectDefinition? created = null;
        await repository.GetAndUpdateAsync(GatewayState.DocumentId, state =>
        {
            if (state.Projects.Any(project =>
                    string.Equals(project.Id, id, StringComparison.OrdinalIgnoreCase)))
            {
                throw GatewayException.InvalidRequest(
                    $"Project '{id}' already exists.",
                    "project_exists",
                    "id");
            }

            created = new ProjectDefinition { Id = id, Name = name };
            state.Projects.Add(created);
        }, cancellationToken);

        projectStorage.Create(id);
        return created!;
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,62}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex ProjectIdPattern();
}
