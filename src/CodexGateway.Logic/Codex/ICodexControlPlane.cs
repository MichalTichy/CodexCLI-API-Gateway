using System.Text.Json;
using CodexGateway.Logic.Storage;

namespace CodexGateway.Logic.Codex;

public interface ICodexControlPlane
{
    Task<IReadOnlyList<CodexModel>> GetModelsAsync(bool forceRefresh, CancellationToken cancellationToken);

    Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken);

    Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken);

    Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken);

    Task CancelDeviceLoginAsync(CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
