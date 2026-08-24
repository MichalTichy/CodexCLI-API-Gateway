using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;
using Shared.Infrastructure.Persistence.Repositories;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record SaveFileUseCase(
    GatewayRequestContext Context,
    string FileName,
    string Purpose,
    Stream Content,
    long DeclaredLength) : IRequest<FileRecord>;

public sealed class SaveFileUseCaseHandler(
    IFileStore files,
    IReadOnlyRepository<GatewayState> repository,
    RunCoordinator coordinator) : IRequestHandler<SaveFileUseCase, FileRecord>
{
    public Task<FileRecord> Handle(SaveFileUseCase request, CancellationToken cancellationToken)
    {
        Validate(request.Context);
        if (request.Context.ProjectId is null)
        {
            return files.SaveAsync(
                null,
                request.Context.ApiKeyId,
                request.FileName,
                request.Purpose,
                request.Content,
                request.DeclaredLength,
                cancellationToken);
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
                return await files.SaveAsync(
                    access.Project.Id,
                    request.Context.ApiKeyId,
                    request.FileName,
                    request.Purpose,
                    request.Content,
                    request.DeclaredLength,
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
