using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit.Sdk;

namespace CodexGateway.OpenAICompatibilityTests.Infrastructure;

public sealed partial class WireCaptureHandler : DelegatingHandler
{
    private readonly List<WireExchange> _exchanges = [];

    public IReadOnlyList<WireExchange> Exchanges
    {
        get
        {
            lock (_exchanges)
            {
                return _exchanges.ToArray();
            }
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var requestBody = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        var response = await base.SendAsync(request, cancellationToken);
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseBody = Encoding.UTF8.GetString(responseBytes);
        var responseContent = new ByteArrayContent(responseBytes);
        foreach (var header in response.Content.Headers)
        {
            responseContent.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = responseContent;
        var exchange = new WireExchange(
            request.Method.Method,
            request.RequestUri?.PathAndQuery ?? string.Empty,
            request.Headers.Authorization?.Scheme,
            request.Content?.Headers.ContentType?.MediaType,
            requestBody,
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            responseBody);
        lock (_exchanges)
        {
            _exchanges.Add(exchange);
        }

        return response;
    }

    public static string FormatRequest(WireExchange exchange)
    {
        using var body = JsonDocument.Parse(exchange.RequestBody);
        return JsonSerializer.Serialize(new
        {
            method = exchange.Method,
            path_and_query = exchange.PathAndQuery,
            authorization = exchange.AuthorizationScheme is null
                ? null
                : exchange.AuthorizationScheme + " <redacted>",
            content_type = exchange.RequestContentType,
            body = body.RootElement
        }, FixtureJsonOptions);
    }

    public static string FormatJsonResponse(WireExchange exchange)
    {
        var body = JsonNode.Parse(exchange.ResponseBody)
            ?? throw new XunitException("The captured response did not contain JSON.");
        if (body is JsonObject responseObject)
        {
            if (responseObject.ContainsKey("id"))
            {
                responseObject["id"] = "<chat-completion-id>";
            }

            if (responseObject.ContainsKey("created"))
            {
                responseObject["created"] = 0;
            }
        }

        return JsonSerializer.Serialize(new
        {
            status = (int)exchange.StatusCode,
            content_type = exchange.ResponseContentType,
            body
        }, FixtureJsonOptions);
    }

    public static string FormatSseResponse(WireExchange exchange)
    {
        var responseBody = CompletionIdRegex().Replace(exchange.ResponseBody, "<chat-completion-id>");
        responseBody = CreatedAtRegex().Replace(responseBody, "\"created\":0");
        responseBody = NormalizeNewLines(responseBody);
        return JsonSerializer.Serialize(new
        {
            status = (int)exchange.StatusCode,
            content_type = exchange.ResponseContentType,
            body = responseBody
        }, FixtureJsonOptions);
    }

    public static void AssertFixture(string fixtureName, string actual)
    {
        var assembly = typeof(WireCaptureHandler).Assembly;
        var suffix = ".Fixtures." + fixtureName;
        var resourceName = assembly.GetManifestResourceNames()
            .SingleOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (resourceName is null)
        {
            throw new XunitException($"Fixture '{fixtureName}' is not embedded.\nActual:\n{actual}");
        }

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new XunitException($"Fixture '{fixtureName}' could not be opened.");
        using var reader = new StreamReader(stream);
        var expected = reader.ReadToEnd();
        expected = NormalizeNewLines(expected).Trim();
        actual = NormalizeNewLines(actual).Trim();
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            throw new XunitException($"Fixture '{fixtureName}' differs.\nExpected:\n{expected}\nActual:\n{actual}");
        }
    }

    private static string NormalizeNewLines(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static readonly JsonSerializerOptions FixtureJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true
    };

    [GeneratedRegex("chatcmpl_[0-9a-f]+", RegexOptions.CultureInvariant)]
    private static partial Regex CompletionIdRegex();

    [GeneratedRegex("\"created\":\\d+", RegexOptions.CultureInvariant)]
    private static partial Regex CreatedAtRegex();
}

public sealed record WireExchange(
    string Method,
    string PathAndQuery,
    string? AuthorizationScheme,
    string? RequestContentType,
    string RequestBody,
    HttpStatusCode StatusCode,
    string? ResponseContentType,
    string ResponseBody);
