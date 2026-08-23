using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files.Models;

public sealed record GetFileUseCase(GatewayRequestContext Context, string FileId) : IRequest<FileRecord>;
