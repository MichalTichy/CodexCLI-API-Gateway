using CodexGateway.Models.Projects;
using MediatR;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed record EnsureSetupProjectUseCase(string Id, string Name) : IRequest<ProjectDefinition>;
