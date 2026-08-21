using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class ArtifactOptions
{
    [Range(1, 100_000)]
    public int MaxFiles { get; set; } = 10_000;

    [Range(1, 16_384)]
    public int MaxFileMegabytes { get; set; } = 512;

    [Range(1, 65_536)]
    public int MaxTotalMegabytes { get; set; } = 2048;
}
