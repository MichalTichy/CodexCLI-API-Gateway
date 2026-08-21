using System.Diagnostics;

namespace CodexGateway.Infrastructure.Codex;

internal static class ContainerEngineEnvironment
{
    private static readonly string[] AllowedVariables =
    [
        "PATH", "PATHEXT", "SystemRoot", "WINDIR", "ComSpec", "SystemDrive",
        "HOME", "USERPROFILE", "XDG_RUNTIME_DIR",
        "DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH",
        "DOCKER_CONFIG", "DOCKER_API_VERSION", "DOCKER_DEFAULT_PLATFORM",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
        "http_proxy", "https_proxy", "no_proxy", "all_proxy"
    ];

    internal static void Configure(
        ProcessStartInfo startInfo,
        IReadOnlyDictionary<string, string?> source,
        IEnumerable<string> secretNames)
    {
        var environment = Build(source, secretNames);
        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }
    }

    internal static IReadOnlyDictionary<string, string> Build(
        IReadOnlyDictionary<string, string?> source,
        IEnumerable<string> secretNames)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        foreach (var name in AllowedVariables)
        {
            CopyIfPresent(source, result, name);
        }

        foreach (var name in secretNames
                     .Where(name => !string.IsNullOrWhiteSpace(name))
                     .Where(name => !CodexProcessEnvironment.IsReservedCredentialVariable(name))
                     .Distinct(comparer))
        {
            CopyIfPresent(source, result, name);
        }

        return result;
    }

    private static void CopyIfPresent(
        IReadOnlyDictionary<string, string?> source,
        IDictionary<string, string> destination,
        string name)
    {
        if (source.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
        {
            destination[name] = value;
        }
    }
}
