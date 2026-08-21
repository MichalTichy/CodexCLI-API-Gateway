using System.Text.RegularExpressions;

namespace CodexGateway.Tests;

public sealed class SourceFileArchitectureTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        ".."));

    private static readonly Regex NonCodePattern = new(
        """(?s)@"(?:""|[^"])*"|"(?:\\.|[^"\\])*"|'(?:\\.|[^'\\])*'|//[^\r\n]*|/\*.*?\*/""",
        RegexOptions.CultureInvariant);

    private static readonly Regex NamespacePattern = new(
        @"^\s*namespace\s+[@A-Za-z_]\w*(?:\s*\.\s*[@A-Za-z_]\w*)*\s*(?<terminator>[;{]?)",
        RegexOptions.CultureInvariant);

    private static readonly Regex TypeDeclarationPattern = new(
        @"^\s*(?:\[[^\]\r\n]+\]\s*)*(?:(?:public|internal|file|abstract|sealed|static|partial|readonly|ref|unsafe)\s+)*(?:class|interface|enum|struct|record(?:\s+(?:class|struct))?|delegate)\s+",
        RegexOptions.CultureInvariant);

    [Fact]
    public void Production_source_files_do_not_declare_multiple_top_level_types()
    {
        var sourceFiles = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(IsProductionSource)
            .ToArray();

        Assert.NotEmpty(sourceFiles);
        foreach (var sourceFile in sourceFiles)
        {
            var declarationCount = CountTopLevelTypeDeclarations(File.ReadAllText(sourceFile));
            Assert.True(
                declarationCount <= 1,
                $"Production source '{Path.GetRelativePath(RepositoryRoot, sourceFile)}' declares " +
                $"{declarationCount} top-level types; expected at most one.");
        }
    }

    private static bool IsProductionSource(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
            !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static int CountTopLevelTypeDeclarations(string source)
    {
        var code = NonCodePattern.Replace(source, match => new string(
            match.Value.Select(character => character is '\r' or '\n' ? character : ' ').ToArray()));
        var braceDepth = 0;
        var namespaceDepth = 0;
        var namespaceBracePending = false;
        var declarationCount = 0;

        foreach (var line in code.Split('\n'))
        {
            if (braceDepth == namespaceDepth && TypeDeclarationPattern.IsMatch(line))
            {
                declarationCount++;
            }

            var namespaceMatch = NamespacePattern.Match(line);
            if (braceDepth == namespaceDepth &&
                namespaceMatch.Success &&
                namespaceMatch.Groups["terminator"].Value != ";")
            {
                namespaceBracePending = true;
            }

            foreach (var character in line)
            {
                if (character == '{')
                {
                    braceDepth++;
                    if (namespaceBracePending)
                    {
                        namespaceDepth++;
                        namespaceBracePending = false;
                    }
                }
                else if (character == '}')
                {
                    if (braceDepth == namespaceDepth)
                    {
                        namespaceDepth--;
                    }

                    braceDepth--;
                }
            }
        }

        return declarationCount;
    }
}
