using System.Text.Json.Serialization;
using CodexGateway.Api.OpenAI;
using CodexGateway.App.Admin;
using CodexGateway.App.Components;
using CodexGateway.App.Mcp;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();
InstallerDiscovery.RunInstallersFromReferencedAssemblies(
    builder.Services,
    builder.Configuration,
    builder.Environment,
    typeof(Program).Assembly);
builder.Services.AddAllInitializers(InstallerDiscovery.DefaultAssemblyNamePrefix);

var app = builder.Build();

app.UseMiddleware<OpenAiRequestMiddleware>();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.UseFastEndpoints(configuration =>
{
    configuration.Serializer.Options.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});
app.UseSwaggerGen();

app.MapGatewayMcpEndpoints();
app.MapDefaultEndpoints();
app.MapStaticAssets();

if (app.Services.GetRequiredService<IOptions<AdminUiOptions>>().Value.Enabled)
{
    app.MapAdminAuthenticationRoutes();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();
}

var initializerManager = app.Services.GetRequiredService<InitializerManager>();
await initializerManager.RunAllInitializersAsync(
    InitializerTrigger.OnStartup,
    InitializerTrigger.OnApplicationReady);

app.Run();

public partial class Program;
