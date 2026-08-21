using System.ClientModel;
using System.ClientModel.Primitives;
using System.Net;
using System.Text;
using System.Text.Json;
using CodexGateway.Logic.UseCases.Projects;
using CodexGateway.Models;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using OpenAI.Chat;

namespace CodexGateway.OpenAICompatibilityTests;

public sealed class OfficialOpenAiClientCompatibilityTests : IDisposable
{
    private const string ProjectId = "openai-compat";
    private readonly CompatibilityGatewayFactory _factory = new();
    private readonly List<HttpClient> _clients = [];

    [Fact]
    public async Task Project_scoped_base_url_and_bearer_key_support_plain_non_streaming_chat()
    {
        await CreateProjectAsync();
        var (client, capture) = CreateChatClient();

        ChatCompletion completion = await client.CompleteChatAsync(
            [new UserChatMessage("Plain client request: hello from Samwise.")]);

        Assert.Equal("Fake Codex response", completion.Content.Single().Text);
        Assert.Equal(ChatFinishReason.Stop, completion.FinishReason);
        var exchange = Assert.Single(capture.Exchanges);
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        Assert.Equal("Bearer", exchange.AuthorizationScheme);
        Assert.Equal($"/p/{ProjectId}/v1/chat/completions", exchange.PathAndQuery);
        WireCaptureHandler.AssertFixture("plain.request.json", WireCaptureHandler.FormatRequest(exchange));
        WireCaptureHandler.AssertFixture("plain.response.json", WireCaptureHandler.FormatJsonResponse(exchange));
    }

    [Fact]
    public async Task Real_client_replays_named_text_history_unicode_multiline_and_content_arrays()
    {
        await CreateProjectAsync();
        var (client, capture) = CreateChatClient();
        SystemChatMessage system = new("Keep answers concise.") { ParticipantName = "system_policy" };
        DeveloperChatMessage developer = new("Use the supplied conversation history.") { ParticipantName = "samwise" };
        UserChatMessage user = new(
            ChatMessageContentPart.CreateTextPart("First line\n"),
            ChatMessageContentPart.CreateTextPart("Příliš žluťoučký kůň 🧭"))
        {
            ParticipantName = "requester_1"
        };
        AssistantChatMessage assistant = new("Earlier assistant answer.") { ParticipantName = "assistant_1" };

        ChatCompletion completion = await client.CompleteChatAsync(
            [system, developer, user, assistant, new UserChatMessage("Continue the conversation.")],
            new ChatCompletionOptions
            {
                ToolChoice = ChatToolChoice.CreateNoneChoice(),
                AllowParallelToolCalls = false,
                ReasoningEffortLevel = ChatReasoningEffortLevel.High
            });

        Assert.Equal("Fake Codex response", completion.Content.Single().Text);
        var exchange = Assert.Single(capture.Exchanges);
        WireCaptureHandler.AssertFixture("history.request.json", WireCaptureHandler.FormatRequest(exchange));
    }

    [Fact]
    public async Task Real_client_reconstructs_gateway_sse_and_observes_terminal_stop()
    {
        await CreateProjectAsync();
        var (client, capture) = CreateChatClient();
        var text = new StringBuilder();
        var roles = new List<ChatMessageRole>();
        var finishReasons = new List<ChatFinishReason>();
        ChatTokenUsage? usage = null;

        await foreach (var update in client.CompleteChatStreamingAsync(
                           [new UserChatMessage("Stream Unicode safely: žluťoučký 🧭")]))
        {
            foreach (var part in update.ContentUpdate)
            {
                text.Append(part.Text);
            }

            if (update.Role is { } role)
            {
                roles.Add(role);
            }

            if (update.FinishReason is { } finishReason)
            {
                finishReasons.Add(finishReason);
            }

            if (update.Usage is { } reportedUsage)
            {
                Assert.Null(usage);
                usage = reportedUsage;
            }
        }

        Assert.Equal("Fake Codex response", text.ToString());
        Assert.Contains(ChatMessageRole.Assistant, roles);
        Assert.Equal(ChatFinishReason.Stop, Assert.Single(finishReasons));
        Assert.NotNull(usage);
        Assert.Equal(10, usage.InputTokenCount);
        Assert.Equal(3, usage.OutputTokenCount);
        Assert.Equal(13, usage.TotalTokenCount);
        var exchange = Assert.Single(capture.Exchanges);
        WireCaptureHandler.AssertFixture("streaming.request.json", WireCaptureHandler.FormatRequest(exchange));
        WireCaptureHandler.AssertFixture("streaming.response.sse", WireCaptureHandler.FormatSseResponse(exchange));
    }

    [Fact]
    public async Task Real_client_sends_json_schema_and_deserializes_structured_content()
    {
        await CreateProjectAsync();
        var (client, capture) = CreateChatClient();
        var schema = BinaryData.FromString("""
            {
              "type": "object",
              "properties": {
                "result": { "type": "string" }
              },
              "required": ["result"],
              "additionalProperties": false
            }
            """);
        var options = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: "samwise_result",
                jsonSchema: schema,
                jsonSchemaFormatDescription: "A deterministic compatibility-test result.",
                jsonSchemaIsStrict: true)
        };

        ChatCompletion completion = await client.CompleteChatAsync(
            [new UserChatMessage("Return the structured compatibility result.")],
            options);

        using var content = JsonDocument.Parse(completion.Content.Single().Text);
        Assert.Equal("ok", content.RootElement.GetProperty("result").GetString());
        var exchange = Assert.Single(capture.Exchanges);
        WireCaptureHandler.AssertFixture("json-schema.request.json", WireCaptureHandler.FormatRequest(exchange));
        WireCaptureHandler.AssertFixture("json-schema.response.json", WireCaptureHandler.FormatJsonResponse(exchange));
    }

    [Fact]
    public async Task Real_client_local_function_tool_fails_with_explicit_gateway_error()
    {
        await CreateProjectAsync();
        var (client, capture) = CreateChatClient();
        var options = new ChatCompletionOptions();
        options.Tools.Add(ChatTool.CreateFunctionTool(
            functionName: "local_write",
            functionDescription: "A local function that V1 must not silently ignore.",
            functionParameters: BinaryData.FromString("""
                {
                  "type": "object",
                  "properties": {},
                  "additionalProperties": false
                }
                """)));

        var exception = await Assert.ThrowsAsync<ClientResultException>(async () =>
            await client.CompleteChatAsync([new UserChatMessage("Call the local tool.")], options));

        Assert.Equal(400, exception.Status);
        var exchange = Assert.Single(capture.Exchanges);
        using var error = JsonDocument.Parse(exchange.ResponseBody);
        Assert.Equal("tools", error.RootElement.GetProperty("error").GetProperty("param").GetString());
        Assert.Equal("unsupported_parameter", error.RootElement.GetProperty("error").GetProperty("code").GetString());
        WireCaptureHandler.AssertFixture("tools.request.json", WireCaptureHandler.FormatRequest(exchange));
        WireCaptureHandler.AssertFixture("tools.response.json", WireCaptureHandler.FormatJsonResponse(exchange));
    }

    public void Dispose()
    {
        foreach (var client in _clients)
        {
            client.Dispose();
        }

        _factory.StopAndDelete();
    }

    private (ChatClient Client, WireCaptureHandler Capture) CreateChatClient()
    {
        var capture = new WireCaptureHandler();
        var httpClient = _factory.CreateDefaultClient(capture);
        _clients.Add(httpClient);
        var endpoint = new Uri(httpClient.BaseAddress!, $"p/{ProjectId}/v1");
        var options = new OpenAIClientOptions
        {
            Endpoint = endpoint,
            Transport = new HttpClientPipelineTransport(httpClient)
        };
        return (new ChatClient("gpt-test-sol", new ApiKeyCredential("e2e-api-key"), options), capture);
    }

    private async Task CreateProjectAsync()
    {
        var sender = _factory.Services.GetRequiredService<ISender>();
        await sender.Send(
            new CreateProjectUseCase(ProjectId, "OpenAI compatibility"),
            CancellationToken.None);
        await sender.Send(new UpdateProjectUseCase(new ProjectDefinition
        {
            Id = ProjectId,
            Name = "OpenAI compatibility",
            Enabled = true,
            ApiKeyAccess =
            {
                new ProjectApiKeyAccess { ApiKeyId = "default" }
            }
        }), CancellationToken.None);
    }
}
