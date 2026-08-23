using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record ListFilesUseCase(GatewayRequestContext Context)
    : IRequest<IReadOnlyList<FileRecord>>;

public sealed class ListFilesUseCaseHandler(
    IFileStore files,
    IReadOnlyRepository<GatewayState> repository,
    RunCoordinator coordinator) : IRequestHandler<ListFilesUseCase, IReadOnlyList<FileRecord>>
{
    public Task<IReadOnlyList<FileRecord>> Handle(
        ListFilesUseCase request,
        CancellationToken cancellationToken)
    {
        Validate(request.Context);
        if (request.Context.ProjectId is null)
        {
            return files.ListAsync(null, request.Context.ApiKeyId, cancellationToken);
        }

        return coordinator.ExecuteProjectOperationAsync(
            request.Context.ProjectId,
            async token =>
            {
                var access = await repository.GetBySpecAsync(
                        new ProjectAccessSpecification(
                            request.Context.ProjectId,
                            request.Context.ApiKeyId),
                        token)
                    ?? throw new InvalidApiKeyException();
                return await files.ListAsync(access.Project.Id, request.Context.ApiKeyId, token);
            },
            cancellationToken);
    }

    private static void Validate(GatewayRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(context.ApiKeyId))
        {
            throw new InvalidApiKeyException();
        }
    }
}
