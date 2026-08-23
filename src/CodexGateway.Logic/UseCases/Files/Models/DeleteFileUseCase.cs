using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record DeleteFileUseCase(GatewayRequestContext Context, string FileId) : IRequest;
