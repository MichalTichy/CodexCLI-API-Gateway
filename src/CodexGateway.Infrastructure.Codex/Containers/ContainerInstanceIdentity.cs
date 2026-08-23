using System.Text;
using System.Text.RegularExpressions;

namespace CodexGateway.Infrastructure.Codex;

internal static partial class ContainerInstanceIdentity
{
    private const string FileName = ".codex-gateway-instance";

    internal static string GetOrCreate(string storageRoot)
    {
        var fullStorageRoot = Path.GetFullPath(storageRoot);
        Directory.CreateDirectory(fullStorageRoot);
        var identityPath = Path.Combine(fullStorageRoot, FileName);
        if (File.Exists(identityPath))
        {
            return Read(identityPath);
        }

        var identity = Guid.NewGuid().ToString("N");
        var temporaryPath = Path.Combine(fullStorageRoot, $"{FileName}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None,
                       bufferSize: 128,
                       System.IO.FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(identity);
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            try
            {
                File.Move(temporaryPath, identityPath, overwrite: false);
                temporaryPath = string.Empty;
                return identity;
            }
            catch (IOException) when (File.Exists(identityPath))
            {
                return Read(identityPath);
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(temporaryPath))
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    internal static void Validate(string identity)
    {
        if (!IdentityPattern().IsMatch(identity))
        {
            throw new InvalidOperationException("The persisted Codex container instance identity is invalid.");
        }
    }

    private static string Read(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("The persisted Codex container instance identity is unsafe.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > 128)
        {
            throw new InvalidOperationException("The persisted Codex container instance identity is invalid.");
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
        var identity = reader.ReadToEnd();
        Validate(identity);
        return identity;
    }

    [GeneratedRegex("^[a-f0-9]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentityPattern();
}
