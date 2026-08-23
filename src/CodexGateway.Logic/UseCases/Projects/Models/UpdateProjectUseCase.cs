using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Projects.Models;

public sealed record UpdateProjectUseCase(ProjectDefinition Project) : IRequest<ProjectDefinition>;
