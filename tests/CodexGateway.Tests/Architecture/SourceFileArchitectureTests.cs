using System.Text.RegularExpressions;

namespace CodexGateway.Tests.Architecture;

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
        @"^\s*namespace\s+(?<name>[@A-Za-z_]\w*(?:\s*\.\s*[@A-Za-z_]\w*)*)\s*(?<terminator>[;{]?)",
        RegexOptions.CultureInvariant | RegexOptions.Multiline);

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
            var source = File.ReadAllText(sourceFile);
            var declarationCount = CountTopLevelTypeDeclarations(source);
            Assert.True(
                declarationCount <= 1 || IsUseCaseAndHandlerPair(sourceFile, source, declarationCount),
                $"Production source '{Path.GetRelativePath(RepositoryRoot, sourceFile)}' declares " +
                $"{declarationCount} top-level types; expected at most one, except for a use case and its handler.");
        }
    }

    [Fact]
    public void Source_namespaces_match_project_folders()
    {
        var sourceFiles = new[] { "SharedInfrastructure", "src", "tests" }
            .SelectMany(directory => Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, directory),
                "*.cs",
                SearchOption.AllDirectories))
            .Where(IsProductionSource)
            .ToArray();

        Assert.NotEmpty(sourceFiles);
        foreach (var sourceFile in sourceFiles)
        {
            var namespaceMatch = NamespacePattern.Match(File.ReadAllText(sourceFile));
            if (!namespaceMatch.Success)
            {
                continue;
            }

            var projectDirectory = FindProjectDirectory(sourceFile);
            var projectFile = Assert.Single(Directory.EnumerateFiles(projectDirectory, "*.csproj"));
            var relativeDirectory = Path.GetRelativePath(projectDirectory, Path.GetDirectoryName(sourceFile)!);
            var expectedNamespace = Path.GetFileNameWithoutExtension(projectFile);
            if (relativeDirectory != ".")
            {
                expectedNamespace += "." + relativeDirectory
                    .Replace(Path.DirectorySeparatorChar, '.')
                    .Replace(Path.AltDirectorySeparatorChar, '.');
            }

            var actualNamespace = Regex.Replace(namespaceMatch.Groups["name"].Value, @"\s+", string.Empty);
            Assert.True(
                actualNamespace == expectedNamespace,
                $"Source '{Path.GetRelativePath(RepositoryRoot, sourceFile)}' uses namespace " +
                $"'{actualNamespace}'; expected '{expectedNamespace}'.");
        }
    }

    [Fact]
    public void Gateway_state_specifications_project_before_materialization()
    {
        var specificationsDirectory = Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.Logic",
            "Specifications");
        var specificationFiles = Directory
            .EnumerateFiles(specificationsDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains(
                "ISpecification<GatewayState",
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(specificationFiles);
        foreach (var specificationFile in specificationFiles)
        {
            var source = File.ReadAllText(specificationFile);
            var queryProjectionMatch = Regex.Match(
                source,
                @"queryable\s*\.Select(?:Many)?\s*\(");

            Assert.True(
                queryProjectionMatch.Success,
                $"Gateway-state specification '{Path.GetRelativePath(RepositoryRoot, specificationFile)}' " +
                "must start its database query with a Select projection.");
            Assert.True(
                Regex.IsMatch(source, @"\.(?:ToList|SingleOrDefault)Async\s*\("),
                $"Gateway-state specification '{Path.GetRelativePath(RepositoryRoot, specificationFile)}' " +
                "must materialize its database query asynchronously.");
        }
    }

    private static bool IsProductionSource(string path)
    {
        var relativePath = Path.GetRelativePath(RepositoryRoot, path);
        var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return !segments.Contains("bin", StringComparer.OrdinalIgnoreCase) &&
            !segments.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static string FindProjectDirectory(string sourceFile)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(sourceFile)!);
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.csproj").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No project file found for '{sourceFile}'.");
    }

    private static bool IsUseCaseAndHandlerPair(string sourceFile, string source, int declarationCount)
    {
        var useCasesDirectory = Path.Combine(RepositoryRoot, "src", "CodexGateway.Logic", "UseCases") +
            Path.DirectorySeparatorChar;
        var useCaseName = Path.GetFileNameWithoutExtension(sourceFile);
        return declarationCount == 2 &&
            sourceFile.StartsWith(useCasesDirectory, StringComparison.OrdinalIgnoreCase) &&
            useCaseName.EndsWith("UseCase", StringComparison.Ordinal) &&
            Regex.IsMatch(source, $@"\brecord\s+{Regex.Escape(useCaseName)}\b") &&
            Regex.IsMatch(source, $@"\bclass\s+{Regex.Escape(useCaseName)}Handler\b");
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
