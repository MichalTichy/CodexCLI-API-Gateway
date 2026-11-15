using CodexGateway.Models.McpServers;
using MediatR;

namespace CodexGateway.Logic.UseCases.Setup;

public sealed record RegisterSetupMcpServerUseCase(McpServerDefinition Server, bool Replace) : IRequest<McpServerDefinition>;
