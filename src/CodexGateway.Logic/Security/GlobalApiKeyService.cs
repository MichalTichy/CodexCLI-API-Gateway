using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CodexGateway.Logic.Configuration;
using CodexGateway.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CodexGateway.Logic.Security;

public sealed class GlobalApiKeyService
{
    private KeyEntry[] _entries = [];
    private static readonly Regex ApiKeyIdPattern = new(
        "^[a-z0-9][a-z0-9_-]{0,63}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    [ActivatorUtilitiesConstructor]
    public GlobalApiKeyService()
    {
    }

    public GlobalApiKeyService(IOptions<GatewayOptions> options)
    {
        Replace((options.Value.ApiKeys ?? []).Select(key => new GatewayApiKeyDefinition
        {
            Id = key.Id,
            Name = key.Name,
            Key = key.Key
        }).ToArray());
    }

    public IReadOnlyList<GlobalApiKeyIdentity> List() =>
        Volatile.Read(ref _entries).Select(entry => entry.Identity).ToArray();

    public GlobalApiKeyIdentity? FindById(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        return Volatile.Read(ref _entries)
            .FirstOrDefault(entry => string.Equals(entry.Identity.Id, id, StringComparison.Ordinal))?.Identity;
    }

    public GlobalApiKeyIdentity? Authenticate(string? rawKey)
    {
        if (string.IsNullOrEmpty(rawKey))
        {
            return null;
        }

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey));
        GlobalApiKeyIdentity? match = null;
        foreach (var entry in Volatile.Read(ref _entries))
        {
            if (CryptographicOperations.FixedTimeEquals(suppliedHash, entry.SecretHash))
            {
                match = entry.Identity;
            }
        }

        return match;
    }

    public void Replace(IReadOnlyCollection<GatewayApiKeyDefinition> apiKeys)
    {
        ArgumentNullException.ThrowIfNull(apiKeys);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var secrets = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<KeyEntry>();
        foreach (var entry in apiKeys)
        {
            if (entry is null ||
                string.IsNullOrWhiteSpace(entry.Id) ||
                string.IsNullOrWhiteSpace(entry.Name) ||
                string.IsNullOrWhiteSpace(entry.Key) ||
                !ApiKeyIdPattern.IsMatch(entry.Id) ||
                entry.Name.Trim().Length > 128 ||
                !string.Equals(entry.Id, entry.Id.Trim(), StringComparison.Ordinal) ||
                !string.Equals(entry.Key, entry.Key.Trim(), StringComparison.Ordinal) ||
                !ids.Add(entry.Id) ||
                !secrets.Add(entry.Key))
            {
                throw new InvalidOperationException("Persisted API key IDs and secrets must be non-empty and unique.");
            }

            entries.Add(CreateEntry(entry.Id, entry.Name.Trim(), entry.Key));
        }

        Volatile.Write(ref _entries, entries.ToArray());
    }

    public static bool IsValidConfiguration(GatewayOptions options)
    {
        try
        {
            new GlobalApiKeyService(Options.Create(options));
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static KeyEntry CreateEntry(string id, string name, string secret) =>
        new(new GlobalApiKeyIdentity(id, name), SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private sealed record KeyEntry(GlobalApiKeyIdentity Identity, byte[] SecretHash);
}
