using CodexGateway.Api;
using CodexGateway.Logic.UseCases.ModelCatalog.Models;
using FastEndpoints;
using MediatR;

namespace CodexGateway.Api.OpenAI.ModelCatalog.Endpoints;

public sealed class ModelsEndpoint(ISender sender) : EndpointWithoutRequest
{
    public override void Configure()
    {
        Verbs(Http.GET);
        Routes("/v1/models", "/p/{projectId}/v1/models");
        AllowAnonymous();
        Description(builder => builder.WithTags("OpenAI"));
    }

    public override async Task HandleAsync(CancellationToken cancellationToken)
    {
        var context = HttpContext.GetGatewayRequestContext();
        var availableModels = await sender.Send(new ListModelsUseCase(context), cancellationToken);
        var response = new
        {
            @object = "list",
            data = availableModels.Select(model => new
            {
                id = model.Id,
                @object = "model",
                created = 0,
                owned_by = "openai",
                supported_reasoning_efforts = model.SupportedReasoningEfforts,
                default_reasoning_effort = model.DefaultReasoningEffort
            })
        };
        await HttpContext.Response.WriteAsJsonAsync(response, OpenAiJson.Options, cancellationToken);
    }
}
