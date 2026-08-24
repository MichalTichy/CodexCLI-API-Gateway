using System.Text.Json;
using CodexGateway.Logic.Specifications;
using CodexGateway.Models;

namespace CodexGateway.Logic.Storage.Models;

public sealed record RunWorkspace(string RootPath, string ArtifactsPath, string? ProjectId);
