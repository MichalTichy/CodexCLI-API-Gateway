using MediatR;

namespace CodexGateway.Logic.UseCases.Files.Models;

public sealed record DeleteFileUseCase(GatewayRequestContext Context, string FileId) : IRequest;
