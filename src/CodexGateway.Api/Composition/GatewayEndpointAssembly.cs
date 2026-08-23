using System.Reflection;
using CodexGateway.IoC;
using CodexGateway.Logic.Configuration;
using FastEndpoints;
using FastEndpoints.Swagger;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Api.Composition;

public sealed record GatewayEndpointAssembly(Assembly Assembly);
