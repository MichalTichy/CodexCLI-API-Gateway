using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class RunLimitOptions
{
    [Range(1, 128)]
    public int MaxConcurrent { get; set; } = 4;

    [Range(0, 4096)]
    public int MaxQueued { get; set; } = 32;

    [Range(1, 1440)]
    public int TimeoutMinutes { get; set; } = 30;

    [Range(1, 86_400)]
    public int? TimeoutSeconds { get; set; }
}
