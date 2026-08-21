using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Files;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Endpoints;

public sealed class ListFilesEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/v1/files", "/p/{projectId}/v1/files");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI Files"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        var records = await sender.Send(new ListFilesUseCase(context), cancellationToken);
        await HttpContext.Response.WriteAsJsonAsync(
            new { @object = "list", data = records.Select(record => new OpenAiFileResponse(record)) },
            OpenAiJson.Options,
            cancellationToken);
    }
}
