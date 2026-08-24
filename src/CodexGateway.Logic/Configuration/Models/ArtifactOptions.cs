using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration.Models;

public sealed class ArtifactOptions
{
    /// <summary>
    /// Maximum number of files allowed in a project artifact set or run workspace.
    /// Exceeding the limit rejects the operation or stops the run before additional output is accepted.
    /// </summary>
    [Range(1, 100_000)]
    public int MaxFiles { get; set; } = 10_000;

    /// <summary>
    /// Maximum size, in megabytes, of any single artifact file.
    /// Files exceeding the limit are rejected even when the total artifact set remains below its limit.
    /// </summary>
    [Range(1, 16_384)]
    public int MaxFileMegabytes { get; set; } = 512;

    /// <summary>
    /// Maximum combined size, in megabytes, of all files in a project artifact set or run workspace.
    /// Exceeding the limit rejects the operation or terminates a run that is producing excessive output.
    /// </summary>
    [Range(1, 65_536)]
    public int MaxTotalMegabytes { get; set; } = 2048;
}
