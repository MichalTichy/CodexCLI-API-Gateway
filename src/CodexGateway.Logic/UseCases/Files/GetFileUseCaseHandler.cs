using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class GetFileUseCaseHandler(GatewayFileService files)
    : IRequestHandler<GetFileUseCase, FileRecord>
{
    public Task<FileRecord> Handle(
        GetFileUseCase request,
        CancellationToken cancellationToken) =>
        files.GetRequiredAsync(request.Context, request.FileId, cancellationToken);
}
