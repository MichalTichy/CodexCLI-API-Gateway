using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record ListFilesUseCase(GatewayRequestContext Context)
    : IRequest<IReadOnlyList<FileRecord>>;
