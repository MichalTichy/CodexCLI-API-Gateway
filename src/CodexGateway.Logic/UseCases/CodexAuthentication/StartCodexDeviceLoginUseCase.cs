using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication;

public sealed record StartCodexDeviceLoginUseCase : IRequest<DeviceLogin>;
