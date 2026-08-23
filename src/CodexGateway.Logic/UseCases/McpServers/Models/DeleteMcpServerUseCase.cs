using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed record DeleteMcpServerUseCase(string ServerId) : IRequest;
