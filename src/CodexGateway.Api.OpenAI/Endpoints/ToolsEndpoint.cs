using System.Security.Cryptography;
using System.Text.Json;
using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.Tools;
using CodexGateway.Logic.UseCases.Tools;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Endpoints;

public sealed class ToolsEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/v1/tools", "/p/{projectId}/v1/tools");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        ValidateDetailRequest();
        var context = HttpContext.GetGatewayRequestContext();
        var catalog = await sender.Send(new GetToolCatalogUseCase(context), cancellationToken);
        var data = BuildData(catalog);
        await WriteResponseAsync(data, cancellationToken);
    }

    private void ValidateDetailRequest()
    {
        var detail = HttpContext.Request.Query["detail"];
        if (detail.Count == 0)
        {
            return;
        }

        if (detail.Count != 1 || !string.Equals(detail[0], "full", StringComparison.Ordinal))
        {
            throw GatewayException.InvalidRequest(
                "The tools detail value must be 'full'.",
                "invalid_detail",
                "detail");
        }
    }

    private Task WriteResponseAsync(
        IReadOnlyList<object> data,
        CancellationToken cancellationToken)
    {
        var response = new { @object = "list", catalog_version = ComputeCatalogVersion(data), data };
        return HttpContext.Response.WriteAsJsonAsync(response, OpenAiJson.Options, cancellationToken);
    }

    private static IReadOnlyList<object> BuildData(ToolCatalog catalog) => catalog.Servers
        .Select(server => (object)new
        {
            id = server.Id,
            name = server.Name,
            version = server.Version,
            required = server.Required,
            title = server.Title,
            description = server.Description,
            website_url = server.WebsiteUrl,
            icons = server.Icons,
            tools = server.Tools.Select(tool => new
            {
                id = tool.Id,
                name = tool.Name,
                title = tool.Title,
                description = tool.Description,
                input_schema = tool.InputSchema,
                output_schema = tool.OutputSchema,
                annotations = tool.Annotations,
                icons = tool.Icons,
                _meta = tool.Meta,
                can_invoke = tool.CanInvoke
            })
        })
        .ToArray();

    internal static string ComputeCatalogVersion(IReadOnlyList<object> data)
    {
        var element = JsonSerializer.SerializeToElement(data, OpenAiJson.Options);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            WriteCanonical(writer, element);
        }

        return "sha256:" + Convert.ToHexString(SHA256.HashData(buffer.ToArray())).ToLowerInvariant();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException("The tool catalog contains an unsupported JSON value.");
        }
    }
}
