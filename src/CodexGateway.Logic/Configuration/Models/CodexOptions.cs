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
    /// Models exposed by the OpenAI-compatible model catalog and accepted for agent runs.
    /// Changing this configuration requires an application restart.
    /// </summary>
    [MinLength(1)]
    public CodexModelOptions[] Models { get; set; } = [];

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
    /// Maximum lifetime of a pending device-login attempt.
    /// On expiry, the login is cancelled and its status becomes expired.
    /// </summary>
    [Range(1, 3600)]
    public int DeviceLoginTimeoutSeconds { get; set; } = 900;
}
