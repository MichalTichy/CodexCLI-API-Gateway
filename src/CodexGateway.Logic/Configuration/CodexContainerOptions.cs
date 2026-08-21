using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class CodexContainerOptions
{
    [Required]
    public string EngineExecutablePath { get; set; } = "docker";

    public string[] EngineArgumentPrefix { get; set; } = [];

    [Required]
    public string Image { get; set; } = "codex-gateway-runner:0.148.0";

    public string? WorkspaceVolume { get; set; }

    public string? AuthVolume { get; set; }

    [Required]
    public string Network { get; set; } = "bridge";

    [Range(128, 262_144)]
    public int MemoryMegabytes { get; set; } = 2048;

    [Range(0.1, 128)]
    public double CpuLimit { get; set; } = 2;

    [Range(16, 1_048_576)]
    public int PidsLimit { get; set; } = 256;

    [Range(16, 65_536)]
    public int TmpfsMegabytes { get; set; } = 256;
}
