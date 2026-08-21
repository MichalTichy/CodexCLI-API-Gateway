using CodexGateway.Logic.Files;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class DeleteFileUseCaseHandler(GatewayFileService files)
    : IRequestHandler<DeleteFileUseCase>
{
    public Task Handle(
        DeleteFileUseCase request,
        CancellationToken cancellationToken) =>
        files.DeleteAsync(request.Context, request.FileId, cancellationToken);
}
