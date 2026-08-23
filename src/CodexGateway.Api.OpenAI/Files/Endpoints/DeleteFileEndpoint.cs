using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Files;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Files.Endpoints;

public sealed class DeleteFileEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.DELETE);
        Routes("/v1/files/{fileId}", "/p/{projectId}/v1/files/{fileId}");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI Files"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        var fileId = HttpContext.Request.RouteValues["fileId"]?.ToString() ?? string.Empty;
        await sender.Send(new DeleteFileUseCase(context, fileId), cancellationToken);
        await HttpContext.Response.WriteAsJsonAsync(
            new { id = fileId, @object = "file", deleted = true },
            OpenAiJson.Options,
            cancellationToken);
    }
}
