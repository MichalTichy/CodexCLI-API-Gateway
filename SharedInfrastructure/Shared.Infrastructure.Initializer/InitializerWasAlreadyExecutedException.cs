namespace Shared.Infrastructure.Initializer;

public sealed class InitializerWasAlreadyExecutedException()
    : InitializerException(
        "The initializer has already run. Multiple executions of the same initializer are not supported.");
