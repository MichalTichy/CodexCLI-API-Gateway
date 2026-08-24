namespace CodexGateway.Infrastructure.Codex.Containers.Models;

internal sealed record ContainerCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
