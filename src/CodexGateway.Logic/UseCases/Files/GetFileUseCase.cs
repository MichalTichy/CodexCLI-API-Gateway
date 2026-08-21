using CodexGateway.Logic.Files;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files;

public sealed record GetFileUseCase(GatewayRequestContext Context, string FileId) : IRequest<FileRecord>;
