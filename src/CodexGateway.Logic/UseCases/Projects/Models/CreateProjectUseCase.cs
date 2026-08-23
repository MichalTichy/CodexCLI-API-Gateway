using System.Text.RegularExpressions;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Projects.Models;

public sealed record CreateProjectUseCase(string Id, string Name) : IRequest<ProjectDefinition>;
