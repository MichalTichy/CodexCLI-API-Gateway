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
    /// Maximum duration of one Codex operation when <see cref="TimeoutSeconds"/> is not set.
    /// When it elapses, the operation is cancelled and its container is stopped.
    /// </summary>
    [Range(1, 1440)]
    public int TimeoutMinutes { get; set; } = 30;

    /// <summary>
    /// Optional second-based override for <see cref="TimeoutMinutes"/>.
    /// Intended for deployments or tests that require a timeout shorter or more precise than whole minutes.
    /// </summary>
    [Range(1, 86_400)]
    public int? TimeoutSeconds { get; set; }
}
