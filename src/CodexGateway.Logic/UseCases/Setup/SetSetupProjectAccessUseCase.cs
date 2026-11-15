using CodexGateway.Models.Projects;
using MediatR;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed record SetSetupProjectAccessUseCase(string ProjectId, ProjectApiKeyAccess Access) : IRequest<ProjectDefinition>;
