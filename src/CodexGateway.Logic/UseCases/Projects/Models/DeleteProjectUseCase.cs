using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.Projects;

public sealed record DeleteProjectUseCase(string ProjectId) : IRequest;
