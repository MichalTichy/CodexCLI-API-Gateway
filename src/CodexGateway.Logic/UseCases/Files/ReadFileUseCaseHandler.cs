using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class ReadFileUseCaseHandler(GatewayFileService files)
    : IRequestHandler<ReadFileUseCase>
{
    public Task Handle(
        ReadFileUseCase request,
        CancellationToken cancellationToken) =>
        files.ReadContentAsync(
            request.Context,
            request.FileId,
            request.Consume,
            cancellationToken);
}
