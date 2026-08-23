using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class CodexContainerOptions
{
    /// <summary>Path or command name of the OCI-compatible container engine used to start agent runs.</summary>
    [Required]
    public string EngineExecutablePath { get; set; } = "docker";

    /// <summary>
    /// Arguments inserted before the gateway-generated container-engine command.
    /// This supports wrappers or remote engine contexts without changing generated run arguments.
    /// </summary>
    public string[] EngineArgumentPrefix { get; set; } = [];

    /// <summary>Container image used for every Codex agent run. The image must already be available to the engine.</summary>
    [Required]
    public string Image { get; set; } = "codex-gateway-runner:0.148.0";

    /// <summary>
    /// Optional existing container volume containing <see cref="GatewayOptions.StoragePath"/>.
    /// When set, run workspaces are mounted from a volume subpath; otherwise their host directories are bind-mounted.
    /// </summary>
    public string? WorkspaceVolume { get; set; }

    /// <summary>
    /// Optional existing container volume used as the Codex home directory inside run containers.
    /// When set, it replaces the bind mount from <see cref="CodexOptions.HomePath"/>.
    /// </summary>
    public string? AuthVolume { get; set; }

    /// <summary>Container network assigned to agent runs, passed directly to the container engine.</summary>
    [Required]
    public string Network { get; set; } = "bridge";

    /// <summary>
    /// Hard memory limit, in megabytes, for each run container. Swap is set to the same value to prevent extra swap allowance.
    /// </summary>
    [Range(128, 262_144)]
    public int MemoryMegabytes { get; set; } = 2048;

    /// <summary>Maximum number of CPU cores available to each run container; fractional values are supported.</summary>
    [Range(0.1, 128)]
    public double CpuLimit { get; set; } = 2;

    /// <summary>Maximum number of processes and threads that may exist inside each run container.</summary>
    [Range(16, 1_048_576)]
    public int PidsLimit { get; set; } = 256;

    /// <summary>
    /// Size, in megabytes, of the in-memory temporary filesystem mounted at <c>/tmp</c>.
    /// It also sizes the temporary Codex home mount used when no persistent authentication volume is required.
    /// </summary>
    [Range(16, 65_536)]
    public int TmpfsMegabytes { get; set; } = 256;
}
