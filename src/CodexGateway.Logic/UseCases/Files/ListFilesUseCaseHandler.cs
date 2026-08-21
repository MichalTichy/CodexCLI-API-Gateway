using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class ListFilesUseCaseHandler(GatewayFileService files)
    : IRequestHandler<ListFilesUseCase, IReadOnlyList<FileRecord>>
{
    public Task<IReadOnlyList<FileRecord>> Handle(
        ListFilesUseCase request,
        CancellationToken cancellationToken) =>
        files.ListAsync(request.Context, cancellationToken);
}
