using CodexGateway.Logic.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CodexGateway.Infrastructure.Storage;

public sealed class StoragePaths
{
    public StoragePaths(IOptions<GatewayOptions> options, IHostEnvironment environment)
    {
        var configured = options.Value.StoragePath;
        Root = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(environment.ContentRootPath, configured));
    }

    public string Root { get; }

    public string Projects => Path.Combine(Root, "projects");

    public string TemporaryFiles => Path.Combine(Root, "temporary-files");

    public string Runs => Path.Combine(Root, "runs");

    public string ProjectRoot(string projectId) => Path.Combine(Projects, projectId);

    public string ProjectArtifacts(string projectId) => Path.Combine(ProjectRoot(projectId), "artifacts");

    public string ProjectFileMetadata(string projectId) => Path.Combine(ProjectRoot(projectId), "files");
}
