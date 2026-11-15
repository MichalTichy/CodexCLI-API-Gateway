using System.Security.Cryptography;
using System.Text;
using CodexGateway.Logic.Errors;

namespace CodexGateway.App.Setup;

public sealed class SetupEndpointFilter(IConfiguration configuration, ILogger<SetupEndpointFilter> logger) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        var configured = configuration["Setup:ApiKey"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return Results.NotFound();
        }

        var supplied = context.HttpContext.Request.Headers["X-Setup-Key"];
        if (supplied.Count != 1 || string.IsNullOrEmpty(supplied[0]) ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(configured)),
                SHA256.HashData(Encoding.UTF8.GetBytes(supplied[0]!))))
        {
            return Results.Unauthorized();
        }

        try
        {
            return await next(context);
        }
        catch (GatewayException exception)
        {
            return Results.Json(new { error = exception.Code, message = exception.Message }, statusCode: exception.StatusCode);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Upstream exceptions can contain connection strings or command environments.
            logger.LogError("Setup operation {Path} failed with {ErrorType}.", context.HttpContext.Request.Path, exception.GetType().Name);
            return Results.Json(new { error = "setup_failed", message = "The setup operation failed. Check service availability and configuration before retrying." }, statusCode: 500);
        }
    }
}
