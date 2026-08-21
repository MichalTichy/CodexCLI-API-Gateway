using System.Text.Json;
using System.Text.Json.Serialization;
using CodexGateway.Logic.Specifications;
using CodexGateway.Logic.Storage;
using CodexGateway.Models;

namespace CodexGateway.Infrastructure.Storage;

public sealed class JsonGatewayConfigurationRepository(StoragePaths paths)
    : IGatewayConfigurationRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<TResult> QueryAsync<TResult>(
        ISpecification<GatewayState, TResult> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            return specification.Apply(await ReadUnsafeAsync(cancellationToken));
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GatewayState> UpdateAsync(
        Func<GatewayState, GatewayState> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var next = update(await ReadUnsafeAsync(cancellationToken));
            await WriteUnsafeAsync(next, cancellationToken);
            return next;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<GatewayState> ReadUnsafeAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Root);
        if (!File.Exists(paths.StateFile))
        {
            return new GatewayState();
        }

        await using var stream = File.OpenRead(paths.StateFile);
        var state = await JsonSerializer.DeserializeAsync<GatewayState>(stream, JsonOptions, cancellationToken)
            ?? new GatewayState();
        return Normalize(state);
    }

    private static GatewayState Normalize(GatewayState state) => state with
    {
        ApiKeys = (state.ApiKeys ?? []).OfType<GatewayApiKeyDefinition>().ToList(),
        Projects = (state.Projects ?? []).OfType<ProjectDefinition>().Select(project => project with
        {
            ApiKeyAccess = (project.ApiKeyAccess ?? []).OfType<ProjectApiKeyAccess>().Select(access => access with
            {
                McpServers = NormalizeAssignments(access.McpServers)
            }).ToList()
        }).ToList(),
        McpServers = (state.McpServers ?? []).OfType<McpServerDefinition>().Select(server => server with
        {
            Arguments = (server.Arguments ?? []).OfType<string>().ToList(),
            EnvironmentVariables = (server.EnvironmentVariables ?? []).OfType<string>().ToList(),
            AvailableTools = (server.AvailableTools ?? []).OfType<string>().ToList()
        }).ToList()
    };

    private static List<ProjectMcpAssignment> NormalizeAssignments(
        IEnumerable<ProjectMcpAssignment>? assignments) =>
        (assignments ?? []).OfType<ProjectMcpAssignment>().Select(assignment => assignment with
        {
            EnabledTools = (assignment.EnabledTools ?? []).OfType<string>().ToList(),
            VisibleTools = assignment.VisibleTools?.OfType<string>().ToList()
        }).ToList();

    private async Task WriteUnsafeAsync(GatewayState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(paths.Root);
        var temporaryPath = paths.StateFile + ".tmp-" + Guid.NewGuid().ToString("N");
        await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            // Encrypt persisted API-key secrets at rest before this gateway is deployed.
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, paths.StateFile, true);
    }
}
