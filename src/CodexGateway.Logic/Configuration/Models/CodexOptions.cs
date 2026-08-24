using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration.Models;

public sealed class CodexOptions
{
    /// <summary>The configuration section bound to these Codex settings.</summary>
    public const string SectionName = "Codex";

    /// <summary>
    /// Path or command name used to start the Codex CLI app server on the gateway host.
    /// This process provides account, login, model, and MCP metadata operations; agent runs execute in containers.
    /// </summary>
    [Required]
    public string ExecutablePath { get; set; } = "codex";

    /// <summary>
    /// Arguments inserted before the gateway-supplied Codex CLI arguments.
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
    /// Number of seconds a successfully retrieved Codex model list is reused.
    /// After it expires, the next request for models queries the Codex app server again and replaces the cached list.
    /// An explicit refresh bypasses the cache before it expires.
    /// </summary>
    [Range(5, 3600)]
    public int ModelCacheSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum time to wait for an individual request to the Codex app server.
    /// When it elapses, that request is cancelled and reported as unavailable; it does not define the agent-run timeout.
    /// </summary>
    [Range(1, 300)]
    public int AppServerRequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum lifetime of a pending device-login attempt.
    /// On expiry, the login is cancelled and its status becomes expired.
    /// </summary>
    [Range(1, 3600)]
    public int DeviceLoginTimeoutSeconds { get; set; } = 900;
}
