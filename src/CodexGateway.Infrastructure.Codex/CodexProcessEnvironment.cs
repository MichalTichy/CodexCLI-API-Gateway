using System.Collections;
using System.Diagnostics;
using CodexGateway.Logic.Codex;

namespace CodexGateway.Infrastructure.Codex;

internal static class CodexProcessEnvironment
{
    private static readonly string[] RuntimeVariables =
    [
        "PATH", "PATHEXT", "SystemRoot", "WINDIR", "ComSpec", "SystemDrive",
        "LANG", "LC_ALL", "LC_CTYPE", "TZ", "SHELL", "USER", "LOGNAME",
        "HTTP_PROXY", "HTTPS_PROXY", "NO_PROXY", "ALL_PROXY",
        "http_proxy", "https_proxy", "no_proxy", "all_proxy",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "NODE_EXTRA_CA_CERTS", "REQUESTS_CA_BUNDLE", "CURL_CA_BUNDLE",
        "LD_LIBRARY_PATH", "DYLD_LIBRARY_PATH"
    ];

    public static void Configure(
        ProcessStartInfo startInfo,
        string codexHome,
        string temporaryDirectory,
        IEnumerable<string> additionalVariableNames)
    {
        var source = Environment.GetEnvironmentVariables()
            .Cast<DictionaryEntry>()
            .Where(entry => entry.Key is string && entry.Value is string)
            .ToDictionary(
                entry => (string)entry.Key,
                entry => (string?)entry.Value,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        var environment = Build(source, codexHome, temporaryDirectory, additionalVariableNames);

        startInfo.Environment.Clear();
        foreach (var (name, value) in environment)
        {
            startInfo.Environment[name] = value;
        }
    }

    internal static IReadOnlyDictionary<string, string> Build(
        IReadOnlyDictionary<string, string?> source,
        string codexHome,
        string temporaryDirectory,
        IEnumerable<string> additionalVariableNames)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var result = new Dictionary<string, string>(comparer);
        foreach (var name in RuntimeVariables)
        {
            CopyIfPresent(source, result, name);
        }

        foreach (var name in additionalVariableNames.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(comparer))
        {
            if (!IsReservedCredentialVariable(name))
            {
                CopyIfPresent(source, result, name);
            }
        }

        var fullCodexHome = Path.GetFullPath(codexHome);
        var fullTemporaryDirectory = Path.GetFullPath(temporaryDirectory);
        result["CODEX_HOME"] = fullCodexHome;
        result["HOME"] = fullCodexHome;
        result["USERPROFILE"] = fullCodexHome;
        result["XDG_CONFIG_HOME"] = fullCodexHome;
        result["XDG_CACHE_HOME"] = Path.Combine(fullCodexHome, ".cache");
        result["TEMP"] = fullTemporaryDirectory;
        result["TMP"] = fullTemporaryDirectory;
        result["TMPDIR"] = fullTemporaryDirectory;
        return result;
    }

    internal static bool IsReservedCredentialVariable(string name) =>
        McpCredentialVariablePolicy.IsReserved(name);

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
