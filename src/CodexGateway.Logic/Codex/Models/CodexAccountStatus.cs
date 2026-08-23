using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record CodexAccountStatus(bool Authenticated, string? AccountType, string? Email);
