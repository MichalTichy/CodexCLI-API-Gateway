using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public sealed record CodexAuthenticationState(
    CodexAccountStatus Account,
    DeviceLogin? Login);
