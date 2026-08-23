using CodexGateway.Logic.Storage;
using MediatR;

namespace CodexGateway.Logic.UseCases.McpServers.Models;

public sealed record DeleteMcpServerUseCase(string ServerId) : IRequest;
