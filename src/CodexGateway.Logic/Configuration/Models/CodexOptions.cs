using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration.Models;

public sealed class CodexOptions
{
    /// <summary>The configuration section bound to these Codex settings.</summary>
    public const string SectionName = "Codex";

    /// <summary>
    /// Path or command name used for gateway-triggered Codex device login, status, and logout commands.
    /// Agent runs use the Codex executable contained in <see cref="Container"/>.<see cref="CodexContainerOptions.Image"/>.
    /// </summary>
    [Required]
    public string ExecutablePath { get; set; } = "codex";

    /// <summary>
    /// Arguments inserted before the gateway-supplied authentication CLI arguments.
    /// This is useful when <see cref="ExecutablePath"/> points to a wrapper such as <c>dotnet</c> or <c>node</c>.
    /// </summary>
    public string[] ArgumentPrefix { get; set; } = [];

    /// <summary>
    /// Codex home directory containing authentication and Codex configuration.
    /// Relative paths are resolved from the application content root and the directory is mounted into run containers.
    /// </summary>
    [Required]
    public string HomePath { get; set; } = ".codex-home";

    /// <summary>Controls the container engine, image, mounts, network, and resource limits used for every agent run.</summary>
    [ValidateObjectMembers]
    public CodexContainerOptions Container { get; set; } = new();

    /// <summary>
    /// Maximum time to wait for the short-lived Codex login-status and logout commands.
    /// It does not limit device login or agent execution.
    /// </summary>
    [Range(1, 300)]
    public int AuthenticationCommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum time to wait for the isolated MCP metadata-discovery process.
    /// On expiry, tool discovery fails without affecting agent-run timeouts.
    /// </summary>
    [Range(1, 300)]
    public int McpDiscoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum time to wait while asking the pinned Codex runner for the models available to the authenticated account.
    /// On expiry, model listing and requests that need to refresh the catalog fail as temporarily unavailable.
    /// </summary>
    [Range(1, 300)]
    public int ModelDiscoveryTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Number of seconds a successfully discovered model catalog may be reused.
    /// After this interval, the next model listing or agent request asks Codex for a fresh account-specific catalog.
    /// A failed refresh is reported as unavailable rather than serving stale model permissions.
    /// </summary>
    [Range(1, 3600)]
    public int ModelCatalogRefreshSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum lifetime of a pending device-login attempt.
    /// On expiry, the login is cancelled and its status becomes expired.
    /// </summary>
    [Range(1, 3600)]
    public int DeviceLoginTimeoutSeconds { get; set; } = 900;
}
