using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.Files.Models;

public sealed record SaveFileUseCase(
    GatewayRequestContext Context,
    string FileName,
    string Purpose,
    Stream Content,
    long DeclaredLength) : IRequest<FileRecord>;
