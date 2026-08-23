using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class ReadFileUseCaseHandler(
    IFileStore files,
    IReadOnlyRepository<GatewayState> repository,
    RunCoordinator coordinator) : IRequestHandler<ReadFileUseCase>
{
    public Task Handle(ReadFileUseCase request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request.Context);
        ArgumentNullException.ThrowIfNull(request.Consume);
        if (string.IsNullOrWhiteSpace(request.Context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }

        return request.Context.ProjectId is null
            ? ReadAsync(null, request, cancellationToken)
            : coordinator.ExecuteProjectOperationAsync(
                request.Context.ProjectId,
                async token =>
                {
                    var access = await repository.GetBySpecAsync(
                            new ProjectAccessSpecification(
                                request.Context.ProjectId,
                                request.Context.ApiKeyId),
                            token)
                        ?? throw new InvalidApiKeyException();
                    await ReadAsync(access.Project.Id, request, token);
                    return true;
                },
                cancellationToken);
    }

    private async Task ReadAsync(
        string? projectId,
        ReadFileUseCase request,
        CancellationToken cancellationToken)
    {
        var (record, content) = await files.OpenAsync(
            projectId,
            request.Context.ApiKeyId,
            request.FileId,
            cancellationToken);
        await using (content)
        {
            await request.Consume(record, content, cancellationToken);
        }
    }
}
