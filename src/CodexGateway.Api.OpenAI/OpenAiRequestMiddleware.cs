using System.Text.Json;
using CodexGateway.Api.OpenAI.Errors;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Security;

namespace CodexGateway.Api.OpenAI;

public sealed class OpenAiRequestMiddleware(
    RequestDelegate next,
    GatewayAccessService access,
    ILogger<OpenAiRequestMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!IsOpenAiRoute(context.Request.Path))
        {
            await next(context);
            return;
        }

        try
        {
            SetRequestId(context);

            var apiKey = access.Authenticate(ReadBearerToken(context.Request))
                ?? throw new InvalidApiKeyException();
            var projectId = ResolveProjectSelector(context.Request);

            var requestContext = await access.ResolveAsync(
                apiKey,
                projectId,
                context.RequestAborted)
                ?? throw new InvalidApiKeyException();

            context.Features.Set(requestContext);
            EnsureSupportedContentType(context.Request);
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            logger.LogInformation("Request {RequestId} was cancelled by the client.", context.TraceIdentifier);
        }
        catch (GatewayException exception)
        {
            await WriteErrorAsync(context, exception.StatusCode, OpenAiErrorResponseMapper.Map(exception));
        }
        catch (BadHttpRequestException exception)
        {
            await WriteErrorAsync(
                context,
                exception.StatusCode,
                "invalid_request",
                "The request could not be parsed.",
                "invalid_request_error",
                null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Unhandled error for request {RequestId}.", context.TraceIdentifier);
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "The gateway encountered an internal error.",
                "server_error",
                null);
        }
    }

    private static bool IsOpenAiRoute(PathString path)
    {
        var value = path.Value ?? string.Empty;
        return value.Equals("/v1", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase) ||
               ReadProjectIdFromPath(path) is not null;
    }

    private static void SetRequestId(HttpContext context)
    {
        var requestId = context.Request.Headers.TryGetValue("x-request-id", out var supplied) &&
                        !string.IsNullOrWhiteSpace(supplied)
            ? supplied.ToString()
            : "req_" + Guid.NewGuid().ToString("N");
        context.TraceIdentifier = requestId;
        context.Response.Headers["x-request-id"] = requestId;
    }

    private static string? ReadBearerToken(HttpRequest request)
    {
        if (request.Headers.Authorization.Count != 1)
        {
            return null;
        }

        var authorization = request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        return authorization.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[prefix.Length..].Trim()
            : null;
    }

    private static string? ResolveProjectSelector(HttpRequest request)
    {
        var projectId = ReadProjectIdFromPath(request.Path);

        var headerValues = request.Headers["OpenAI-Project"];
        if (headerValues.Count == 0)
        {
            return projectId;
        }

        if (headerValues.Count != 1 || string.IsNullOrWhiteSpace(headerValues[0]))
        {
            throw CreateInvalidProjectHeaderError();
        }

        var headerProjectId = headerValues[0]!.Trim();
        if (!IsValidProjectId(headerProjectId))
        {
            throw CreateInvalidProjectHeaderError();
        }

        if (projectId is not null &&
            !string.Equals(projectId, headerProjectId, StringComparison.OrdinalIgnoreCase))
        {
            throw GatewayException.InvalidRequest(
                "The project in the URL does not match the OpenAI-Project header.",
                "project_mismatch",
                "OpenAI-Project");
        }

        return projectId ?? headerProjectId;
    }

    private static GatewayException CreateInvalidProjectHeaderError() =>
        GatewayException.InvalidRequest(
            "The OpenAI-Project header is invalid.",
            "invalid_project",
            "OpenAI-Project");

    private static void EnsureSupportedContentType(HttpRequest request)
    {
        if (!HttpMethods.IsPost(request.Method))
        {
            return;
        }

        var path = request.Path.Value ?? string.Empty;
        if (path.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            if (request.ContentType?.StartsWith("application/json", StringComparison.OrdinalIgnoreCase) != true)
            {
                throw CreateUnsupportedMediaTypeError();
            }

            return;
        }

        if (path.EndsWith("/files", StringComparison.OrdinalIgnoreCase) && !request.HasFormContentType)
        {
            throw CreateUnsupportedMediaTypeError();
        }
    }

    private static GatewayException CreateUnsupportedMediaTypeError() => new(
        GatewayErrorCategory.InvalidInput,
        StatusCodes.Status415UnsupportedMediaType,
        "unsupported_media_type",
        "The request Content-Type is not supported for this endpoint.");

    private static string? ReadProjectIdFromPath(PathString path)
    {
        var value = path.Value;
        if (value is null || !value.StartsWith("/p/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var projectStart = "/p/".Length;
        var projectEnd = value.IndexOf('/', projectStart);
        if (projectEnd <= projectStart)
        {
            return null;
        }

        var suffix = value[projectEnd..];
        if (!suffix.Equals("/v1", StringComparison.OrdinalIgnoreCase) &&
            !suffix.StartsWith("/v1/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value[projectStart..projectEnd];
    }

    private static bool IsValidProjectId(string value)
    {
        if (value.Length is < 2 or > 64 ||
            !IsAsciiLetterOrDigit(value[0]) ||
            !IsAsciiLetterOrDigit(value[^1]))
        {
            return false;
        }

        return value.All(character => IsAsciiLetterOrDigit(character) || character == '-');
    }

    private static bool IsAsciiLetterOrDigit(char character) =>
        character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9';

    private static Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        string type,
        string? parameter) =>
        WriteErrorAsync(context, statusCode, new OpenAiError(message, type, parameter, code));

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        OpenAiError error)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        if (statusCode == StatusCodes.Status401Unauthorized &&
            string.Equals(error.Code, "invalid_api_key", StringComparison.Ordinal))
        {
            context.Response.Headers.WWWAuthenticate = "Bearer";
        }

        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json";
        var payload = OpenAiErrorResponseMapper.Response(error);
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            payload,
            cancellationToken: context.RequestAborted);
    }
}
