using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Files;

public sealed class DeleteFileUseCaseHandler(
    IFileStore files,
    IReadOnlyRepository<GatewayState> repository,
    RunCoordinator coordinator) : IRequestHandler<DeleteFileUseCase>
{
    public Task Handle(DeleteFileUseCase request, CancellationToken cancellationToken)
    {
        Validate(request.Context);
        if (request.Context.ProjectId is null)
        {
            return files.DeleteAsync(null, request.Context.ApiKeyId, request.FileId, cancellationToken);
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
                await files.DeleteAsync(access.Project.Id, request.Context.ApiKeyId, request.FileId, token);
                return true;
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
