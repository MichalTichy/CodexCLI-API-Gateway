using System.Reflection;
using CodexGateway.Logic.Configuration;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Api.Composition;

public sealed class GatewayApiInstaller : ILowPriorityInstaller
{
    private const long Megabyte = 1024L * 1024L;

    public void Install(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var endpointAssemblies = services
            .Where(descriptor => descriptor.ServiceType == typeof(GatewayEndpointAssembly))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GatewayEndpointAssembly>()
            .Select(registration => registration.Assembly)
            .GroupBy(
                assembly => assembly.FullName ?? assembly.GetName().Name ?? string.Empty,
                StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(
                assembly => assembly.FullName ?? assembly.GetName().Name,
                StringComparer.Ordinal)
            .ToArray();

        if (endpointAssemblies.Length == 0)
        {
            throw new InvalidOperationException(
                "At least one gateway endpoint assembly must be registered before the API installer runs.");
        }

        var maxUploadMegabytes = configuration.GetValue<long?>(
            $"{GatewayOptions.SectionName}:Files:MaxUploadMegabytes") ?? 25;
        var maxRequestBodyBytes = maxUploadMegabytes * Megabyte + Megabyte;

        services.Configure<FormOptions>(options =>
            options.MultipartBodyLengthLimit = maxRequestBodyBytes);
        services.Configure<KestrelServerOptions>(options =>
            options.Limits.MaxRequestBodySize = maxRequestBodyBytes);

        services
            .AddFastEndpoints(options =>
            {
                options.Assemblies = endpointAssemblies;
                options.DisableAutoDiscovery = true;
            })
            .SwaggerDocument(settings =>
            {
                settings.DocumentSettings = document =>
                {
                    document.Title = "Codex CLI API Gateway";
                    document.Version = "v1";
                };
            });
    }
}
