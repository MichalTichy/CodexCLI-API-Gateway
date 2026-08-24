using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Files;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Files.Endpoints;

public sealed class DownloadFileEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/v1/files/{fileId}/content", "/p/{projectId}/v1/files/{fileId}/content");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI Files"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        var fileId = HttpContext.Request.RouteValues["fileId"]?.ToString() ?? string.Empty;
        await sender.Send(
            new ReadFileUseCase(
                context,
                fileId,
                async (record, content, token) =>
                {
                    HttpContext.Response.ContentType = "application/octet-stream";
                    HttpContext.Response.ContentLength = record.Bytes;
                    HttpContext.Response.Headers.ContentDisposition =
                        $"attachment; filename=\"{record.FileName.Replace("\"", string.Empty, StringComparison.Ordinal)}\"";
                    await content.CopyToAsync(HttpContext.Response.Body, token);
                }),
            cancellationToken);
    }
}
