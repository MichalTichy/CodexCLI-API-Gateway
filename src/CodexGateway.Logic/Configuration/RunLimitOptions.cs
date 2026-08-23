using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Configuration;

public sealed class RunLimitOptions
{
    /// <summary>
    /// Maximum number of Codex operations that may execute concurrently across the gateway.
    /// Additional accepted operations wait in the queue.
    /// </summary>
    [Range(1, 128)]
    public int MaxConcurrent { get; set; } = 4;

    /// <summary>
    /// Maximum number of operations allowed to wait after all concurrent slots are occupied.
    /// New operations are rejected immediately when both the running and queued capacities are full.
    /// </summary>
    [Range(0, 4096)]
    public int MaxQueued { get; set; } = 32;

    /// <summary>
    /// Maximum duration, in seconds, of one Codex operation.
    /// When it elapses, the operation is cancelled and its container is stopped.
    /// </summary>
    [Range(1, 86_400)]
    public int TimeoutSeconds { get; set; } = 1_800;
}
