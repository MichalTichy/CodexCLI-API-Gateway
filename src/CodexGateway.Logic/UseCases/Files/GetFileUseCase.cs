using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record GetFileUseCase(GatewayRequestContext Context, string FileId) : IRequest<FileRecord>;

public sealed class GetFileUseCaseHandler(
    IFileStore files,
    IReadOnlyRepository<GatewayState> repository,
    RunCoordinator coordinator) : IRequestHandler<GetFileUseCase, FileRecord>
{
    public Task<FileRecord> Handle(GetFileUseCase request, CancellationToken cancellationToken)
    {
        Validate(request.Context);
        if (request.Context.ProjectId is null)
        {
            return files.GetRequiredAsync(null, request.Context.ApiKeyId, request.FileId, cancellationToken);
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
                return await files.GetRequiredAsync(
                    access.Project.Id,
                    request.Context.ApiKeyId,
                    request.FileId,
                    token);
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
