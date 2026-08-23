using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files.Models;

public sealed record ReadFileUseCase(
    GatewayRequestContext Context,
    string FileId,
    Func<FileRecord, Stream, CancellationToken, Task> Consume) : IRequest;
