using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex.Models;

public sealed record DeviceLogin(
    string LoginId,
    string VerificationUrl,
    string UserCode,
    DeviceLoginStatus Status,
    string? Error = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? ExpiresAt = null);
