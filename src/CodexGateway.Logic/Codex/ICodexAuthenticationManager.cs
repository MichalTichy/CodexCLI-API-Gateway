namespace CodexGateway.Logic.Codex;

public interface ICodexAuthenticationManager
{
    Task<CodexAccountStatus> GetAccountAsync(CancellationToken cancellationToken);

    Task<DeviceLogin> StartDeviceLoginAsync(CancellationToken cancellationToken);

    Task<DeviceLogin?> GetDeviceLoginAsync(CancellationToken cancellationToken);

    Task CancelDeviceLoginAsync(CancellationToken cancellationToken);

    Task LogoutAsync(CancellationToken cancellationToken);
}
