using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class CodexOptions
{
    public const string SectionName = "Codex";

    [Required]
    public string ExecutablePath { get; set; } = "codex";

    public string[] ArgumentPrefix { get; set; } = [];

    [Required]
    public string HomePath { get; set; } = ".codex-home";

    [ValidateObjectMembers]
    public CodexContainerOptions Container { get; set; } = new();

    [Range(5, 3600)]
    public int ModelCacheSeconds { get; set; } = 300;

    [Range(1, 300)]
    public int AppServerRequestTimeoutSeconds { get; set; } = 30;

    [Range(1, 60)]
    public int DeviceLoginTimeoutMinutes { get; set; } = 15;

    [Range(1, 3600)]
    public int? DeviceLoginTimeoutSeconds { get; set; }
}
