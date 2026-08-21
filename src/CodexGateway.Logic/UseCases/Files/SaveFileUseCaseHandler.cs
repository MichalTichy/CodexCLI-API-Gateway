using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class SaveFileUseCaseHandler(GatewayFileService files)
    : IRequestHandler<SaveFileUseCase, FileRecord>
{
    public Task<FileRecord> Handle(
        SaveFileUseCase request,
        CancellationToken cancellationToken) =>
        files.SaveAsync(
            request.Context,
            request.FileName,
            request.Purpose,
            request.Content,
            request.DeclaredLength,
            cancellationToken);
}
