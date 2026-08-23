using CodexGateway.Api;
using CodexGateway.Logic.Errors;
using CodexGateway.Logic.UseCases.Files;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.Files.Endpoints;

public sealed class UploadFileEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.POST);
        Routes("/v1/files", "/p/{projectId}/v1/files");
        AllowAnonymous();
        AllowFileUploads();
        Description(builder => builder.WithTags("OpenAI Files"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        IFormCollection form;
        try
        {
            form = await HttpContext.Request.ReadFormAsync(cancellationToken);
        }
        catch (InvalidDataException)
        {
            throw GatewayException.InvalidRequest("The multipart form body is invalid.", parameter: "file");
        }

        var uploaded = form.Files.GetFile("file")
            ?? throw GatewayException.InvalidRequest("A multipart file field named 'file' is required.", parameter: "file");
        await using var stream = uploaded.OpenReadStream();
        var record = await sender.Send(
            new SaveFileUseCase(
                context,
                uploaded.FileName,
                form["purpose"].ToString(),
                stream,
                uploaded.Length),
            cancellationToken);
        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        await HttpContext.Response.WriteAsJsonAsync(
            new OpenAiFileResponse(record),
            OpenAiJson.Options,
            cancellationToken);
    }
}
