using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files.Models;

public sealed record ListFilesUseCase(GatewayRequestContext Context)
    : IRequest<IReadOnlyList<FileRecord>>;
