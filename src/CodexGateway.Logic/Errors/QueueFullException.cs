namespace CodexGateway.Logic.Errors;

public sealed class QueueFullException()
    : GatewayException(
        GatewayErrorCategory.CapacityExceeded,
        429,
        "queue_full",
        "The gateway run queue is full.");
