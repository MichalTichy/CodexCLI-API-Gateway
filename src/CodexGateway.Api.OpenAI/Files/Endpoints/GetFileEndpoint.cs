using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Files;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Endpoints;

public sealed class GetFileEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/v1/files/{fileId}", "/p/{projectId}/v1/files/{fileId}");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI Files"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        var fileId = HttpContext.Request.RouteValues["fileId"]?.ToString() ?? string.Empty;
        var record = await sender.Send(new GetFileUseCase(context, fileId), cancellationToken);
        await HttpContext.Response.WriteAsJsonAsync(
            new OpenAiFileResponse(record),
            OpenAiJson.Options,
            cancellationToken);
    }
}
