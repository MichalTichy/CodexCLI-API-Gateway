namespace CodexGateway.Logic.Errors;

public enum GatewayErrorCategory
{
    InvalidInput,
    Unauthenticated,
    Forbidden,
    NotFound,
    Conflict,
    PayloadTooLarge,
    CapacityExceeded,
    Timeout,
    Unavailable,
    UpstreamFailure,
    Internal
}
