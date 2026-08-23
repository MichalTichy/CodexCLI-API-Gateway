using System.Text.RegularExpressions;
using CodexGateway.Logic.Codex;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;
using MediatR;

namespace CodexGateway.Logic.UseCases.McpServers;

public sealed record CreateOrUpdateMcpServerUseCase(McpServerDefinition Server)
    : IRequest<McpServerDefinition>;
