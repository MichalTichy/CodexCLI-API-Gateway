using System.Text.Json;
using CodexGateway.Api;
using CodexGateway.Api.OpenAI.Errors;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Generation;
using CodexGateway.Logic.UseCases.Generation;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Endpoints;

public sealed class ChatCompletionsEndpoint(
    ISender sender,
    OpenAiChatCompletionMapper mapper) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/v1/chat/completions", "/p/{projectId}/v1/chat/completions");
        AllowAnonymous();
        Description(builder => builder
            .WithTags("OpenAI")
            .Accepts<ChatCompletionRequest>("application/json"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        ChatCompletionRequest request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<ChatCompletionRequest>(
                    HttpContext.Request.Body,
                    OpenAiJson.Options,
                    cancellationToken)
                ?? throw GatewayException.InvalidRequest(
                    "The request is invalid.",
                    "invalid_request");
        }
        catch (JsonException)
        {
            throw GatewayException.InvalidRequest(
                "The request is invalid.",
                "invalid_request");
        }

        if (string.IsNullOrWhiteSpace(request.Model))
        {
            throw GatewayException.InvalidRequest("A model is required.", "model_required", "model");
        }

        var validatedResponseFormat = ChatCompletionRequestValidator.Validate(request);
        var context = HttpContext.GetGatewayRequestContext();
        var input = mapper.ToAssistantResponseInput(request, context, validatedResponseFormat);
        var completionId = "chatcmpl_" + Guid.NewGuid().ToString("N");
        var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (request.Stream)
        {
            await StreamAsync(completionId, created, input, request, cancellationToken);
            return;
        }

        var result = await sender.Send(
            new GenerateAssistantResponseUseCase(input),
            cancellationToken);
        var response = new
        {
            id = completionId,
            @object = "chat.completion",
            created,
            model = result.ModelId,
            choices = new[]
            {
                new
                {
                    index = 0,
                    message = new { role = "assistant", content = result.Text },
                    finish_reason = "stop"
                }
            },
            usage = BuildUsage(result.Usage)
        };
        await HttpContext.Response.WriteAsJsonAsync(response, OpenAiJson.Options, cancellationToken);
    }

    private async Task StreamAsync(
        string completionId,
        long created,
        AssistantResponseInput input,
        ChatCompletionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            var observer = new OpenAiGenerationObserver(HttpContext, completionId, created);
            var result = await sender.Send(
                new GenerateAssistantResponseUseCase(input, observer),
                cancellationToken);

            await WriteSseAsync(HttpContext, new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created,
                model = result.ModelId,
                choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }
            }, cancellationToken);

            if (request.StreamOptions?.IncludeUsage == true)
            {
                await WriteSseAsync(HttpContext, new
                {
                    id = completionId,
                    @object = "chat.completion.chunk",
                    created,
                    model = result.ModelId,
                    choices = Array.Empty<object>(),
                    usage = BuildUsage(result.Usage)
                }, cancellationToken);
            }
        }
        catch (GatewayException) when (!HttpContext.Response.HasStarted)
        {
            throw;
        }
        catch (GatewayException exception)
        {
            await WriteSseAsync(
                HttpContext,
                OpenAiErrorResponseMapper.Response(exception),
                cancellationToken);
        }

        await HttpContext.Response.WriteAsync("data: [DONE]\n\n", cancellationToken);
        await HttpContext.Response.Body.FlushAsync(cancellationToken);
    }

    private static async Task WriteSseAsync(
        HttpContext context,
        object value,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(value, OpenAiJson.Options);
        await context.Response.WriteAsync("data: " + json + "\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    private static object BuildUsage(GenerationUsage usage) => new
    {
        prompt_tokens = usage.InputTokens,
        completion_tokens = usage.OutputTokens,
        total_tokens = usage.TotalTokens,
        prompt_tokens_details = new { cached_tokens = usage.CachedInputTokens },
        completion_tokens_details = new { reasoning_tokens = usage.ReasoningOutputTokens }
    };

    private sealed class OpenAiGenerationObserver(
        HttpContext context,
        string completionId,
        long created) : IGenerationObserver
    {
        private string? _modelId;

        public async ValueTask StartedAsync(
            GenerationStarted started,
            CancellationToken cancellationToken)
        {
            _modelId = started.ModelId;
            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers.Connection = "keep-alive";
            await WriteSseAsync(context, new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created,
                model = started.ModelId,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { role = "assistant" },
                        finish_reason = (string?)null
                    }
                }
            }, cancellationToken);
        }

        public async ValueTask TextDeltaAsync(string text, CancellationToken cancellationToken)
        {
            await WriteSseAsync(context, new
            {
                id = completionId,
                @object = "chat.completion.chunk",
                created,
                model = _modelId,
                choices = new[]
                {
                    new
                    {
                        index = 0,
                        delta = new { content = text },
                        finish_reason = (string?)null
                    }
                }
            }, cancellationToken);
        }
    }
}
