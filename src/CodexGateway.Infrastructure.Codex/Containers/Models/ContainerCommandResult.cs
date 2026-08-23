namespace CodexGateway.Infrastructure.Codex;

internal sealed record ContainerCommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
