using CodexGateway.Logic.Codex;
using MediatR;

namespace CodexGateway.Logic.UseCases.CodexAuthentication.Models;

public sealed record StartCodexDeviceLoginUseCase : IRequest<DeviceLogin>;
