using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record DeviceLogin(
    string LoginId,
    string VerificationUrl,
    string UserCode,
    DeviceLoginStatus Status,
    string? Error = null);
