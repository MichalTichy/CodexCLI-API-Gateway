using System.Reflection;
using System.Xml.Linq;

namespace CodexGateway.Tests.Architecture;

public sealed class ArchitectureDependencyTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..",
        "..",
        "..",
        "..",
        ".."));

    [Fact]
    public void API_projects_do_not_reference_the_host_or_infrastructure()
    {
        var apiProjects = new[] { ProjectPath("CodexGateway.Api") }
            .Concat(FindAdapterProjects())
            .ToArray();

        Assert.Contains(ProjectPath("CodexGateway.Api.OpenAI"), apiProjects);
        foreach (var project in apiProjects)
        {
            var references = ReadProjectReferences(project);
            Assert.DoesNotContain("CodexGateway.App", references);
            Assert.DoesNotContain(references, reference =>
                reference.StartsWith("CodexGateway.Infrastructure", StringComparison.Ordinal) ||
                reference.StartsWith("CodexGateway.McpGateway", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("CodexGateway.Logic")]
    [InlineData("CodexGateway.Infrastructure.Codex")]
    [InlineData("CodexGateway.Infrastructure.FileStorage")]
    public void Inward_layers_do_not_reference_API_projects(string projectName)
    {
        var references = ReadProjectReferences(ProjectPath(projectName));

        Assert.DoesNotContain(references, reference =>
            reference.Equals("CodexGateway.Api", StringComparison.Ordinal) ||
            reference.StartsWith("CodexGateway.Api.", StringComparison.Ordinal));
    }

    [Fact]
    public void API_adapters_do_not_reference_each_other()
    {
        var adapterProjects = FindAdapterProjects();
        var adapterNames = adapterProjects
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.NotEmpty(adapterProjects);
        Assert.DoesNotContain(ReadProjectReferences(ProjectPath("CodexGateway.Api")), adapterNames.Contains);
        foreach (var adapterProject in adapterProjects)
        {
            var references = ReadProjectReferences(adapterProject);
            Assert.DoesNotContain(references, adapterNames.Contains);
        }
    }

    [Theory]
    [InlineData(typeof(CodexGateway.App.Composition.AppInstaller))]
    [InlineData(typeof(CodexGateway.Api.Composition.GatewayApiInstaller))]
    [InlineData(typeof(CodexGateway.Logic.Composition.LogicInstaller))]
    [InlineData(typeof(CodexGateway.Infrastructure.Codex.Composition.CodexInfrastructureInstaller))]
    [InlineData(typeof(CodexGateway.Infrastructure.FileStorage.Composition.FileStorageInfrastructureInstaller))]
    [InlineData(typeof(CodexGateway.McpGateway.Composition.McpGatewayInstaller))]
    [InlineData(typeof(CodexGateway.McpGateway.Http.Composition.HttpMcpGatewayInstaller))]
    [InlineData(typeof(CodexGateway.McpGateway.Stdio.Composition.StdioMcpGatewayInstaller))]
    public void Every_DI_module_owns_one_public_installer(Type expectedInstaller)
    {
        var installers = expectedInstaller.Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(IInstaller).IsAssignableFrom(type))
            .ToArray();

        var installer = Assert.Single(installers);
        Assert.Equal(expectedInstaller, installer);
        Assert.True(installer.IsPublic);
        Assert.NotNull(installer.GetConstructor(Type.EmptyTypes));
    }

    [Fact]
    public void Every_API_adapter_owns_one_public_installer()
    {
        foreach (var adapterProject in FindAdapterProjects())
        {
            var assemblyName = Path.GetFileNameWithoutExtension(adapterProject);
            var assembly = Assembly.Load(new AssemblyName(assemblyName));
            var installers = assembly
                .GetTypes()
                .Where(type => type.IsClass && !type.IsAbstract && typeof(IInstaller).IsAssignableFrom(type))
                .ToArray();

            var installer = Assert.Single(installers);
            Assert.True(installer.IsPublic);
            Assert.NotNull(installer.GetConstructor(Type.EmptyTypes));
        }
    }

    [Fact]
    public void Infrastructure_is_split_with_dependencies_pointing_from_codex_to_storage()
    {
        var codexReferences = ReadProjectReferences(ProjectPath("CodexGateway.Infrastructure.Codex"));
        Assert.Contains("CodexGateway.Infrastructure.FileStorage", codexReferences);
        Assert.Contains("Shared.Infrastructure.IoC", codexReferences);

        var storageReferences = ReadProjectReferences(ProjectPath("CodexGateway.Infrastructure.FileStorage"));
        Assert.DoesNotContain("CodexGateway.Infrastructure.Codex", storageReferences);
        Assert.Contains("Shared.Infrastructure.IoC", storageReferences);

        Assert.False(File.Exists(ProjectPath("CodexGateway.Infrastructure")));
    }

    [Fact]
    public void MCP_gateway_core_and_transports_are_separate_projects()
    {
        var coreReferences = ReadProjectReferences(ProjectPath("CodexGateway.McpGateway"));
        Assert.DoesNotContain("CodexGateway.McpGateway.Http", coreReferences);
        Assert.DoesNotContain("CodexGateway.McpGateway.Stdio", coreReferences);

        var httpReferences = ReadProjectReferences(ProjectPath("CodexGateway.McpGateway.Http"));
        Assert.Contains("CodexGateway.McpGateway", httpReferences);
        Assert.DoesNotContain("CodexGateway.McpGateway.Stdio", httpReferences);

        var stdioReferences = ReadProjectReferences(ProjectPath("CodexGateway.McpGateway.Stdio"));
        Assert.Contains("CodexGateway.McpGateway", stdioReferences);
        Assert.DoesNotContain("CodexGateway.McpGateway.Http", stdioReferences);

        var appReferences = ReadProjectReferences(ProjectPath("CodexGateway.App"));
        Assert.Contains("CodexGateway.McpGateway", appReferences);
        Assert.Contains("CodexGateway.McpGateway.Http", appReferences);
        Assert.Contains("CodexGateway.McpGateway.Stdio", appReferences);

        Assert.False(Directory.Exists(ProjectPath("CodexGateway.Infrastructure.Mcp")));
    }

    [Fact]
    public void Behavioral_Razor_components_use_code_behind_files()
    {
        var componentsRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.App",
            "Components");
        var razorFiles = Directory.EnumerateFiles(
                Path.Combine(componentsRoot, "Pages"),
                "*.razor",
                SearchOption.AllDirectories)
            .ToArray();

        Assert.NotEmpty(razorFiles);
        foreach (var razorFile in razorFiles)
        {
            Assert.True(
                File.Exists(razorFile + ".cs"),
                $"Behavioral component '{razorFile}' must have a matching .razor.cs file.");

            var markup = File.ReadAllText(razorFile);
            Assert.DoesNotContain("@code", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("@inject", markup, StringComparison.Ordinal);
            Assert.DoesNotContain("@implements", markup, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Behavioral_admin_code_behinds_use_mediator_directly()
    {
        var adminComponentsRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.App",
            "Components",
            "Pages",
            "Admin");
        string[] behavioralComponents =
        [
            "AdminDashboard.razor.cs",
            "CodexAccountCard.razor.cs",
            "McpCatalogSection.razor.cs",
            "McpServerEditor.razor.cs",
            "ProjectEditor.razor.cs",
            "ProjectsSection.razor.cs"
        ];

        foreach (var component in behavioralComponents)
        {
            var path = Assert.Single(Directory.EnumerateFiles(
                adminComponentsRoot,
                component,
                SearchOption.AllDirectories));
            var source = File.ReadAllText(path);
            Assert.Contains("ISender Sender", source, StringComparison.Ordinal);
            Assert.Contains("Sender.Send(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AdminSessionGuard", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Session.ExecuteAsync(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("AdminOperations", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SemaphoreSlim", source, StringComparison.Ordinal);
        }

        var dashboardPath = Assert.Single(Directory.EnumerateFiles(
            adminComponentsRoot,
            "AdminDashboard.razor.cs",
            SearchOption.AllDirectories));
        var dashboard = File.ReadAllText(dashboardPath);
        Assert.DoesNotContain("_loadGate", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("_codexLoadGate", dashboard, StringComparison.Ordinal);

        var appRoot = Path.Combine(RepositoryRoot, "src", "CodexGateway.App");
        Assert.False(File.Exists(Path.Combine(appRoot, "Security", "AdminSessionGuard.cs")));
        var componentSources = Directory.EnumerateFiles(
            Path.Combine(appRoot, "Components"),
            "*.cs",
            SearchOption.AllDirectories).ToArray();
        Assert.DoesNotContain(
            componentSources,
            path => string.Equals(
                Path.GetFileName(path),
                "AdminOperations.cs",
                StringComparison.Ordinal));
        Assert.DoesNotContain(componentSources, path =>
        {
            var source = File.ReadAllText(path);
            return source.Contains("AdminSessionGuard", StringComparison.Ordinal) ||
                source.Contains("AdminOperations", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Management_actions_are_named_logic_requests_with_handlers()
    {
        string[] expectedUseCases =
        [
            "CreateProjectUseCase",
            "UpdateProjectUseCase",
            "DeleteProjectUseCase",
            "CreateOrUpdateMcpServerUseCase",
            "DeleteMcpServerUseCase",
            "GetCodexAuthenticationStateUseCase",
            "StartCodexDeviceLoginUseCase",
            "CancelCodexDeviceLoginUseCase",
            "LogoutCodexAccountUseCase"
        ];
        var logicTypes = typeof(CodexGateway.Logic.Composition.LogicInstaller).Assembly.GetTypes();

        foreach (var expectedUseCase in expectedUseCases)
        {
            var requestType = Assert.Single(
                logicTypes,
                type => string.Equals(type.Name, expectedUseCase, StringComparison.Ordinal));
            Assert.True(
                requestType.Namespace?.StartsWith("CodexGateway.Logic.UseCases.", StringComparison.Ordinal) == true,
                $"Management request '{requestType.FullName}' must live under Logic.UseCases.");
            Assert.True(
                IsMediatRRequest(requestType),
                $"Management use case '{requestType.FullName}' must implement MediatR IRequest.");
            Assert.Single(
                logicTypes,
                type => type.IsClass && !type.IsAbstract && HandlesRequest(type, requestType));
        }
    }

    [Fact]
    public void Gateway_configuration_uses_the_shared_Marten_repository_contract()
    {
        var logicAssembly = typeof(CodexGateway.Logic.Composition.LogicInstaller).Assembly;
        var storageAssembly = typeof(CodexGateway.Infrastructure.FileStorage.Composition.FileStorageInfrastructureInstaller).Assembly;
        var repositoryContract = typeof(Shared.Infrastructure.Persistence.Repositories.IRepository<>);
        var repositoryImplementation = typeof(
            Shared.Infrastructure.Persistence.Marten.Repository.Document.NoTenancyMartenRepository<>);

        Assert.True(repositoryContract.IsInterface);
        Assert.Contains(
            repositoryImplementation.GetInterfaces(),
            type => type.IsGenericType
                    && type.GetGenericTypeDefinition() == repositoryContract);
        Assert.Null(logicAssembly.GetType("CodexGateway.Logic.Storage.IGatewayStateStore"));
        Assert.Null(logicAssembly.GetType("CodexGateway.Logic.Storage.IGatewayConfigurationRepository"));
        Assert.Null(storageAssembly.GetType("CodexGateway.Infrastructure.FileStorage.JsonStateStore"));
        Assert.Null(storageAssembly.GetType("CodexGateway.Infrastructure.FileStorage.JsonGatewayConfigurationRepository"));

        var productionSources = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .ToArray();
        Assert.DoesNotContain(productionSources, path =>
            File.ReadAllText(path).Contains("IGatewayStateStore", StringComparison.Ordinal));
        Assert.DoesNotContain(productionSources, path =>
            File.ReadAllText(path).Contains("JsonStateStore", StringComparison.Ordinal));
        Assert.DoesNotContain(productionSources, path =>
            File.ReadAllText(path).Contains("IGatewayConfigurationRepository", StringComparison.Ordinal));
    }

    [Fact]
    public void Host_composition_uses_discovery_instead_of_module_specific_registration_calls()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.App",
            "Program.cs"));

        Assert.Contains(
            "InstallerDiscovery.RunInstallersFromReferencedAssemblies(",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("AddCodexGatewayLogic(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddCodexGatewayInfrastructure(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddGatewayApi(", program, StringComparison.Ordinal);
        Assert.DoesNotContain("AddOpenAiApi(", program, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenAI_request_pipeline_is_explicit_in_the_host()
    {
        var program = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.App",
            "Program.cs"));

        Assert.Contains(
            "app.UseMiddleware<OpenAiRequestMiddleware>();",
            program,
            StringComparison.Ordinal);
        Assert.DoesNotContain("UseOpenAiApi(", program, StringComparison.Ordinal);

        var adapterRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "CodexGateway.Api.OpenAI");
        Assert.False(File.Exists(Path.Combine(adapterRoot, "OpenAiEndpointHelpers.cs")));
        Assert.False(File.Exists(Path.Combine(adapterRoot, "Security", "OpenAiRequestPolicy.cs")));
        Assert.False(File.Exists(Path.Combine(adapterRoot, "ApplicationBuilderExtensions.cs")));
    }

    [Fact]
    public void MediatR_is_pinned_to_the_pre_license_key_release()
    {
        var references = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(path => XDocument.Load(path).Descendants("PackageReference"))
            .Where(element => string.Equals(
                element.Attribute("Include")?.Value,
                "MediatR",
                StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(references);
        Assert.All(references, reference =>
            Assert.Equal("[12.5.0]", reference.Attribute("Version")?.Value));
    }

    [Fact]
    public void App_pins_MediatR_to_the_pre_license_key_release()
    {
        var references = XDocument
            .Load(ProjectPath("CodexGateway.App"))
            .Descendants("PackageReference")
            .Where(element => string.Equals(
                element.Attribute("Include")?.Value,
                "MediatR",
                StringComparison.Ordinal))
            .ToArray();

        var reference = Assert.Single(references);
        Assert.Equal("[12.5.0]", reference.Attribute("Version")?.Value);
    }

    private static string ProjectPath(string projectName) =>
        Path.Combine(RepositoryRoot, "src", projectName, projectName + ".csproj");

    private static string[] FindAdapterProjects() => Directory
        .EnumerateDirectories(Path.Combine(RepositoryRoot, "src"), "CodexGateway.Api.*", SearchOption.TopDirectoryOnly)
        .Where(directory => Path.GetFileName(directory).StartsWith("CodexGateway.Api.", StringComparison.Ordinal))
        .Select(directory => Path.Combine(directory, Path.GetFileName(directory) + ".csproj"))
        .Where(File.Exists)
        .ToArray();

    private static IReadOnlyList<string> ReadProjectReferences(string projectPath)
    {
        Assert.True(File.Exists(projectPath), $"Expected project '{projectPath}' to exist.");

        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!
                .Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar))
            .Select(path => Path.GetFileNameWithoutExtension(path)!)
            .ToArray();
    }

    private static bool IsMediatRRequest(Type type) => type.GetInterfaces().Any(contract =>
        string.Equals(contract.FullName, "MediatR.IRequest", StringComparison.Ordinal) ||
        contract.IsGenericType &&
        string.Equals(
            contract.GetGenericTypeDefinition().FullName,
            "MediatR.IRequest`1",
            StringComparison.Ordinal));

    private static bool HandlesRequest(Type handlerType, Type requestType) => handlerType
        .GetInterfaces()
        .Where(contract => contract.IsGenericType)
        .Where(contract => contract.GetGenericTypeDefinition().FullName is
            "MediatR.IRequestHandler`1" or "MediatR.IRequestHandler`2")
        .Any(contract => contract.GetGenericArguments()[0] == requestType);
}
