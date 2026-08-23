using System.Reflection;
using System.Reflection.Emit;
using CodexGateway.IoC;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.Tests.Architecture;

public sealed class InstallerDiscoveryTests
{
    [Fact]
    public void RunInstallers_orders_deterministically_and_deduplicates_assemblies()
    {
        InstallerExecutionRecorder.Reset();
        var firstAssembly = DynamicInstallerAssembly.Create(
            "A",
            new("NormalZulu", "normal-a-z"),
            new("LowAlpha", "low-a", Priority: InstallerPriority.Low),
            new("HighZulu", "high-a-z", Priority: InstallerPriority.High),
            new("HighAlpha", "high-a-a", Priority: InstallerPriority.High));
        var secondAssembly = DynamicInstallerAssembly.Create(
            "Z",
            new("NormalAlpha", "normal-z-a"),
            new("HighAlpha", "high-z-a", Priority: InstallerPriority.High),
            new("LowAlpha", "low-z-a", Priority: InstallerPriority.Low));
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        var environment = new TestHostEnvironment();

        InstallerDiscovery.RunInstallers(
            services,
            configuration,
            environment,
            [secondAssembly, firstAssembly, firstAssembly]);

        var invocations = InstallerExecutionRecorder.Invocations;
        Assert.Equal(
            ["high-a-a", "high-a-z", "high-z-a", "normal-a-z", "normal-z-a", "low-a", "low-z-a"],
            invocations.Select(invocation => invocation.Name));
        Assert.All(invocations, invocation => Assert.Same(services, invocation.Services));
        Assert.All(invocations, invocation => Assert.Same(configuration, invocation.Configuration));
        Assert.All(invocations, invocation => Assert.Same(environment, invocation.Environment));
    }

    [Fact]
    public void RunInstallers_skips_abstract_and_open_generic_installers()
    {
        InstallerExecutionRecorder.Reset();
        var assembly = DynamicInstallerAssembly.Create(
            "Filtering",
            new("AbstractInstaller", "abstract", IsAbstract: true),
            new("OpenGenericInstaller", "open-generic", IsOpenGeneric: true),
            new("ConcreteInstaller", "concrete"));

        Run(assembly);

        var invocation = Assert.Single(InstallerExecutionRecorder.Invocations);
        Assert.Equal("concrete", invocation.Name);
    }

    [Fact]
    public void RunInstallers_rejects_an_installer_with_both_priorities()
    {
        var assembly = DynamicInstallerAssembly.Create(
            "ConflictingPriority",
            new InstallerDefinition("ConflictingInstaller", "unused", Priority: InstallerPriority.Both));

        var exception = Assert.Throws<InvalidOperationException>(() => Run(assembly));

        Assert.Contains("ConflictingInstaller", exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IHighPriorityInstaller), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(ILowPriorityInstaller), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RunInstallers_wraps_constructor_failures_with_installer_identity()
    {
        InstallerExecutionRecorder.Reset();
        var assembly = DynamicInstallerAssembly.Create(
            "BrokenConstructor",
            new("AWorkingInstaller", "must-not-run"),
            new InstallerDefinition(
                "ZBrokenConstructorInstaller",
                "unused",
                Behavior: InstallerBehavior.ThrowFromConstructor));

        var exception = Assert.Throws<InvalidOperationException>(() => Run(assembly));

        Assert.Contains("ZBrokenConstructorInstaller", exception.Message, StringComparison.Ordinal);
        Assert.Contains("public parameterless constructor", exception.Message, StringComparison.Ordinal);
        Assert.Equal("constructor exploded", exception.InnerException?.Message);
        Assert.Empty(InstallerExecutionRecorder.Invocations);
    }

    [Fact]
    public void RunInstallers_wraps_install_failures_with_installer_identity()
    {
        var assembly = DynamicInstallerAssembly.Create(
            "BrokenInstall",
            new InstallerDefinition(
                "BrokenInstallInstaller",
                "unused",
                Behavior: InstallerBehavior.ThrowFromInstall));

        var exception = Assert.Throws<InvalidOperationException>(() => Run(assembly));

        Assert.Contains("BrokenInstallInstaller", exception.Message, StringComparison.Ordinal);
        Assert.Contains("failed while registering services", exception.Message, StringComparison.Ordinal);
        Assert.Equal("install exploded", exception.InnerException?.Message);
    }

    [Fact]
    public void RunInstallers_wraps_reflection_errors_with_assembly_and_loader_details()
    {
        var assembly = new BrokenTypesAssembly();

        var exception = Assert.Throws<InvalidOperationException>(() => Run(assembly));

        Assert.Contains("CodexGateway.BrokenTypes", exception.Message, StringComparison.Ordinal);
        Assert.Contains("loader exploded", exception.Message, StringComparison.Ordinal);
        Assert.IsType<ReflectionTypeLoadException>(exception.InnerException);
    }

    [Fact]
    public void RunInstallersFromReferencedAssemblies_discovers_modules_from_the_anchor_dependency_context()
    {
        var services = new ServiceCollection();

        InstallerDiscovery.RunInstallersFromReferencedAssemblies(
            services,
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment { EnvironmentName = "Testing" },
            typeof(Program).Assembly);

        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName ==
                          "CodexGateway.Api.OpenAI.ChatCompletions.OpenAiChatCompletionMapper");
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName ==
                          "CodexGateway.Logic.Codex.RunCoordinator");
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.IsGenericType
                          && descriptor.ServiceType.GetGenericTypeDefinition() ==
                          typeof(Shared.Infrastructure.Persistence.Repositories.IRepository<>));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType.FullName ==
                          "CodexGateway.Infrastructure.Codex.Containers.ContainerCodexRunner");

        var hostedServiceTypes = services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService))
            .Select(descriptor => descriptor.ImplementationType)
            .ToArray();
        var preflightIndex = Array.IndexOf(
            hostedServiceTypes,
            typeof(CodexGateway.Infrastructure.Codex.Containers.ContainerRuntimePreflightService));
        var temporaryCleanupIndex = Array.IndexOf(
            hostedServiceTypes,
            typeof(CodexGateway.Infrastructure.Storage.Files.TemporaryFileCleanupService));
        Assert.True(preflightIndex >= 0, "The container runtime preflight hosted service was not registered.");
        Assert.True(temporaryCleanupIndex >= 0, "The temporary-file cleanup hosted service was not registered.");
        Assert.True(
            preflightIndex < temporaryCleanupIndex,
            "Container runtime preflight must start before temporary-file cleanup.");
    }

    [Fact]
    public void RunInstallersFromReferencedAssemblies_requires_a_prefix()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            InstallerDiscovery.RunInstallersFromReferencedAssemblies(
                new ServiceCollection(),
                new ConfigurationBuilder().Build(),
                new TestHostEnvironment(),
                typeof(InstallerDiscoveryTests).Assembly,
                " "));

        Assert.Equal("assemblyNamePrefix", exception.ParamName);
    }

    private static void Run(params Assembly[] assemblies)
    {
        InstallerDiscovery.RunInstallers(
            new ServiceCollection(),
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment(),
            assemblies);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CodexGateway.IoC.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class BrokenTypesAssembly : Assembly
    {
        public override string FullName => "CodexGateway.BrokenTypes, Version=1.0.0.0";

        public override AssemblyName GetName(bool copiedName) => new("CodexGateway.BrokenTypes");

        public override Type[] GetTypes() => throw new ReflectionTypeLoadException(
            [null!],
            [new TypeLoadException("loader exploded")]);
    }
}

public static class InstallerExecutionRecorder
{
    private static readonly object Sync = new();
    private static readonly List<InstallerInvocation> RecordedInvocations = [];

    public static IReadOnlyList<InstallerInvocation> Invocations
    {
        get
        {
            lock (Sync)
            {
                return RecordedInvocations.ToArray();
            }
        }
    }

    public static void Record(
        string name,
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        lock (Sync)
        {
            RecordedInvocations.Add(new InstallerInvocation(name, services, configuration, environment));
        }
    }

    public static void Reset()
    {
        lock (Sync)
        {
            RecordedInvocations.Clear();
        }
    }
}

public sealed record InstallerInvocation(
    string Name,
    IServiceCollection Services,
    IConfiguration Configuration,
    IHostEnvironment Environment);

internal static class DynamicInstallerAssembly
{
    private static readonly ConstructorInfo ObjectConstructor =
        typeof(object).GetConstructor(Type.EmptyTypes)!;

    private static readonly ConstructorInfo InvalidOperationExceptionConstructor =
        typeof(InvalidOperationException).GetConstructor([typeof(string)])!;

    private static readonly MethodInfo RecordMethod =
        typeof(InstallerExecutionRecorder).GetMethod(nameof(InstallerExecutionRecorder.Record))!;

    private static readonly MethodInfo InstallMethod =
        typeof(IInstaller).GetMethod(nameof(IInstaller.Install))!;

    public static Assembly Create(string sortableName, params InstallerDefinition[] definitions)
    {
        var assemblyName = new AssemblyName(
            $"CodexGateway.DynamicInstallerTests.{sortableName}.{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(
            assemblyName,
            AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule(assemblyName.Name!);

        foreach (var definition in definitions)
        {
            CreateInstallerType(moduleBuilder, definition);
        }

        return assemblyBuilder;
    }

    private static void CreateInstallerType(
        ModuleBuilder moduleBuilder,
        InstallerDefinition definition)
    {
        var attributes = TypeAttributes.Public | TypeAttributes.Class;
        if (definition.IsAbstract)
        {
            attributes |= TypeAttributes.Abstract;
        }

        var typeBuilder = moduleBuilder.DefineType(
            $"DynamicInstallers.{definition.TypeName}",
            attributes);

        if (definition.IsOpenGeneric)
        {
            typeBuilder.DefineGenericParameters("T");
        }

        AddInstallerInterfaces(typeBuilder, definition.Priority);
        DefineConstructor(typeBuilder, definition.Behavior);
        DefineInstallMethod(typeBuilder, definition);
        typeBuilder.CreateType();
    }

    private static void AddInstallerInterfaces(TypeBuilder typeBuilder, InstallerPriority priority)
    {
        switch (priority)
        {
            case InstallerPriority.Normal:
                typeBuilder.AddInterfaceImplementation(typeof(IInstaller));
                break;
            case InstallerPriority.High:
                typeBuilder.AddInterfaceImplementation(typeof(IHighPriorityInstaller));
                break;
            case InstallerPriority.Low:
                typeBuilder.AddInterfaceImplementation(typeof(ILowPriorityInstaller));
                break;
            case InstallerPriority.Both:
                typeBuilder.AddInterfaceImplementation(typeof(IHighPriorityInstaller));
                typeBuilder.AddInterfaceImplementation(typeof(ILowPriorityInstaller));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(priority), priority, null);
        }
    }

    private static void DefineConstructor(
        TypeBuilder typeBuilder,
        InstallerBehavior behavior)
    {
        var constructor = typeBuilder.DefineConstructor(
            MethodAttributes.Public,
            CallingConventions.Standard,
            Type.EmptyTypes);
        var il = constructor.GetILGenerator();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, ObjectConstructor);

        if (behavior == InstallerBehavior.ThrowFromConstructor)
        {
            il.Emit(OpCodes.Ldstr, "constructor exploded");
            il.Emit(OpCodes.Newobj, InvalidOperationExceptionConstructor);
            il.Emit(OpCodes.Throw);
            return;
        }

        il.Emit(OpCodes.Ret);
    }

    private static void DefineInstallMethod(
        TypeBuilder typeBuilder,
        InstallerDefinition definition)
    {
        var method = typeBuilder.DefineMethod(
            nameof(IInstaller.Install),
            MethodAttributes.Public
            | MethodAttributes.Final
            | MethodAttributes.HideBySig
            | MethodAttributes.NewSlot
            | MethodAttributes.Virtual,
            typeof(void),
            [
                typeof(IServiceCollection),
                typeof(IConfiguration),
                typeof(IHostEnvironment)
            ]);
        var il = method.GetILGenerator();

        if (definition.Behavior == InstallerBehavior.ThrowFromInstall)
        {
            il.Emit(OpCodes.Ldstr, "install exploded");
            il.Emit(OpCodes.Newobj, InvalidOperationExceptionConstructor);
            il.Emit(OpCodes.Throw);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, definition.RecordName);
            il.Emit(OpCodes.Ldarg_1);
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldarg_3);
            il.Emit(OpCodes.Call, RecordMethod);
            il.Emit(OpCodes.Ret);
        }

        typeBuilder.DefineMethodOverride(method, InstallMethod);
    }
}

internal sealed record InstallerDefinition(
    string TypeName,
    string RecordName,
    InstallerPriority Priority = InstallerPriority.Normal,
    InstallerBehavior Behavior = InstallerBehavior.Record,
    bool IsAbstract = false,
    bool IsOpenGeneric = false);

internal enum InstallerPriority
{
    High,
    Normal,
    Low,
    Both
}

internal enum InstallerBehavior
{
    Record,
    ThrowFromConstructor,
    ThrowFromInstall
}
