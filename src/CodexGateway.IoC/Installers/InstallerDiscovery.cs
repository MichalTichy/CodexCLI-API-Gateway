using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyModel;
using Microsoft.Extensions.Hosting;

namespace CodexGateway.IoC.Installers;

public static class InstallerDiscovery
{
    public const string DefaultAssemblyNamePrefix = "CodexGateway.";

    public static void RunInstallersFromReferencedAssemblies(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        Assembly anchorAssembly,
        string assemblyNamePrefix = DefaultAssemblyNamePrefix)
    {
        ArgumentNullException.ThrowIfNull(anchorAssembly);

        if (string.IsNullOrWhiteSpace(assemblyNamePrefix))
        {
            throw new ArgumentException("An assembly name prefix is required.", nameof(assemblyNamePrefix));
        }

        var assemblies = DiscoverReferencedAssemblies(anchorAssembly, assemblyNamePrefix);
        RunInstallers(services, configuration, environment, assemblies);
    }

    public static void RunInstallers(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(assemblies);

        var installers = GetInstallerDescriptors(assemblies)
            .Select(descriptor => (Descriptor: descriptor, Installer: CreateInstaller(descriptor)))
            .ToArray();

        foreach (var (descriptor, installer) in installers)
        {
            try
            {
                installer.Install(services, configuration, environment);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Installer '{descriptor.Type.FullName}' from assembly " +
                    $"'{descriptor.AssemblyIdentity}' failed while registering services.",
                    exception);
            }
        }
    }

    private static IReadOnlyList<Assembly> DiscoverReferencedAssemblies(
        Assembly anchorAssembly,
        string assemblyNamePrefix)
    {
        var loadContext = AssemblyLoadContext.GetLoadContext(anchorAssembly) ?? AssemblyLoadContext.Default;
        var baseDirectories = GetBaseDirectories(anchorAssembly);
        var discovered = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pendingNames = new Queue<AssemblyName>();
        var inspectedAssemblyIdentities = new HashSet<string>(StringComparer.Ordinal);
        var queuedAssemblyIdentities = new HashSet<string>(StringComparer.Ordinal);

        AddAssembly(anchorAssembly, discovered);
        EnqueueReferences(anchorAssembly);

        var dependencyContext = DependencyContext.Load(anchorAssembly);
        if (dependencyContext is not null)
        {
            foreach (var assemblyName in dependencyContext.RuntimeLibraries
                         .SelectMany(library => library.GetDefaultAssemblyNames(dependencyContext))
                         .Where(name => HasPrefix(name.Name, assemblyNamePrefix))
                         .OrderBy(GetAssemblyIdentity, StringComparer.Ordinal))
            {
                Enqueue(assemblyName);
            }
        }

        while (pendingNames.TryDequeue(out var assemblyName))
        {
            var assembly = LoadAssembly(loadContext, assemblyName, baseDirectories);
            AddAssembly(assembly, discovered);
            EnqueueReferences(assembly);
        }

        return discovered.Values
            .Where(assembly => HasPrefix(assembly.GetName().Name, assemblyNamePrefix))
            .OrderBy(GetAssemblyIdentity, StringComparer.Ordinal)
            .ToArray();

        void EnqueueReferences(Assembly assembly)
        {
            var identity = GetAssemblyIdentity(assembly);
            if (!inspectedAssemblyIdentities.Add(identity))
            {
                return;
            }

            AssemblyName[] references;
            try
            {
                references = assembly.GetReferencedAssemblies();
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Failed to inspect references of installer assembly '{identity}'.",
                    exception);
            }

            foreach (var reference in references
                         .Where(name => HasPrefix(name.Name, assemblyNamePrefix))
                         .OrderBy(GetAssemblyIdentity, StringComparer.Ordinal))
            {
                Enqueue(reference);
            }
        }

        void Enqueue(AssemblyName assemblyName)
        {
            if (queuedAssemblyIdentities.Add(GetAssemblyIdentity(assemblyName)))
            {
                pendingNames.Enqueue(assemblyName);
            }
        }
    }

    private static IReadOnlyList<InstallerDescriptor> GetInstallerDescriptors(
        IEnumerable<Assembly> assemblies)
    {
        var uniqueAssemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            if (assembly is null)
            {
                throw new ArgumentException("The assembly collection cannot contain null values.", nameof(assemblies));
            }

            uniqueAssemblies.TryAdd(GetAssemblyIdentity(assembly), assembly);
        }

        var descriptors = new List<InstallerDescriptor>();

        foreach (var assembly in uniqueAssemblies.Values.OrderBy(GetAssemblyIdentity, StringComparer.Ordinal))
        {
            foreach (var installerType in GetTypes(assembly)
                         .Where(IsConcreteInstallerType)
                         .OrderBy(GetTypeIdentity, StringComparer.Ordinal))
            {
                var isHighPriority = typeof(IHighPriorityInstaller).IsAssignableFrom(installerType);
                var isLowPriority = typeof(ILowPriorityInstaller).IsAssignableFrom(installerType);

                if (isHighPriority && isLowPriority)
                {
                    throw new InvalidOperationException(
                        $"Installer '{installerType.FullName}' from assembly " +
                        $"'{GetAssemblyIdentity(assembly)}' cannot implement both " +
                        $"{nameof(IHighPriorityInstaller)} and {nameof(ILowPriorityInstaller)}.");
                }

                var priority = isHighPriority
                    ? InstallerPriority.High
                    : isLowPriority
                        ? InstallerPriority.Low
                        : InstallerPriority.Normal;

                descriptors.Add(new InstallerDescriptor(
                    installerType,
                    GetAssemblyIdentity(assembly),
                    GetTypeIdentity(installerType),
                    priority));
            }
        }

        return descriptors
            .OrderBy(descriptor => descriptor.Priority)
            .ThenBy(descriptor => descriptor.AssemblyIdentity, StringComparer.Ordinal)
            .ThenBy(descriptor => descriptor.TypeIdentity, StringComparer.Ordinal)
            .ToArray();
    }

    private static Type[] GetTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            var loaderErrors = exception.LoaderExceptions
                .Where(loaderException => loaderException is not null)
                .Select(loaderException => loaderException!.Message)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var details = loaderErrors.Length == 0
                ? string.Empty
                : $" Loader errors: {string.Join(" | ", loaderErrors)}";

            throw new InvalidOperationException(
                $"Failed to inspect assembly '{GetAssemblyIdentity(assembly)}' for installers.{details}",
                exception);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Failed to inspect assembly '{GetAssemblyIdentity(assembly)}' for installers.",
                exception);
        }
    }

    private static IInstaller CreateInstaller(InstallerDescriptor descriptor)
    {
        object? instance;

        try
        {
            instance = Activator.CreateInstance(descriptor.Type);
        }
        catch (Exception exception)
        {
            var cause = exception is TargetInvocationException { InnerException: not null }
                ? exception.InnerException
                : exception;

            throw new InvalidOperationException(
                $"Failed to create installer '{descriptor.Type.FullName}' from assembly " +
                $"'{descriptor.AssemblyIdentity}'. Installers must have a public parameterless constructor.",
                cause);
        }

        if (instance is not IInstaller installer)
        {
            throw new InvalidOperationException(
                $"Failed to create installer '{descriptor.Type.FullName}' from assembly " +
                $"'{descriptor.AssemblyIdentity}'.");
        }

        return installer;
    }

    private static Assembly LoadAssembly(
        AssemblyLoadContext loadContext,
        AssemblyName assemblyName,
        IReadOnlyList<string> baseDirectories)
    {
        var loaded = loadContext.Assemblies.FirstOrDefault(
            assembly => string.Equals(
                GetAssemblyIdentity(assembly),
                GetAssemblyIdentity(assemblyName),
                StringComparison.Ordinal));
        if (loaded is not null)
        {
            return loaded;
        }

        Exception? nameLoadException = null;
        try
        {
            return loadContext.LoadFromAssemblyName(assemblyName);
        }
        catch (Exception exception) when (exception is FileNotFoundException or FileLoadException)
        {
            nameLoadException = exception;
        }

        foreach (var baseDirectory in baseDirectories)
        {
            var candidatePath = Path.Combine(baseDirectory, $"{assemblyName.Name}.dll");
            if (!File.Exists(candidatePath))
            {
                continue;
            }

            try
            {
                return loadContext.LoadFromAssemblyPath(Path.GetFullPath(candidatePath));
            }
            catch (Exception pathLoadException)
            {
                throw new InvalidOperationException(
                    $"Failed to load installer assembly '{GetAssemblyIdentity(assemblyName)}' " +
                    $"from '{candidatePath}'.",
                    new AggregateException(nameLoadException!, pathLoadException));
            }
        }

        throw new InvalidOperationException(
            $"Failed to load referenced installer assembly '{GetAssemblyIdentity(assemblyName)}'. " +
            $"Searched: {string.Join(", ", baseDirectories)}.",
            nameLoadException);
    }

    private static IReadOnlyList<string> GetBaseDirectories(Assembly anchorAssembly)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetFullPath(AppContext.BaseDirectory)
        };

        if (!string.IsNullOrWhiteSpace(anchorAssembly.Location))
        {
            var anchorDirectory = Path.GetDirectoryName(anchorAssembly.Location);
            if (!string.IsNullOrWhiteSpace(anchorDirectory))
            {
                directories.Add(Path.GetFullPath(anchorDirectory));
            }
        }

        return directories.Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddAssembly(Assembly assembly, IDictionary<string, Assembly> assemblies)
    {
        assemblies.TryAdd(GetAssemblyIdentity(assembly), assembly);
    }

    private static bool IsConcreteInstallerType(Type type) =>
        type.IsClass
        && !type.IsAbstract
        && !type.ContainsGenericParameters
        && typeof(IInstaller).IsAssignableFrom(type);

    private static bool HasPrefix(string? assemblyName, string assemblyNamePrefix) =>
        assemblyName?.StartsWith(assemblyNamePrefix, StringComparison.Ordinal) is true;

    private static string GetAssemblyIdentity(Assembly assembly) =>
        assembly.FullName ?? assembly.GetName().Name ?? "<unknown assembly>";

    private static string GetAssemblyIdentity(AssemblyName assemblyName) =>
        assemblyName.FullName ?? assemblyName.Name ?? "<unknown assembly>";

    private static string GetTypeIdentity(Type type) => type.FullName ?? type.Name;

    private sealed record InstallerDescriptor(
        Type Type,
        string AssemblyIdentity,
        string TypeIdentity,
        InstallerPriority Priority);

    private enum InstallerPriority
    {
        High,
        Normal,
        Low
    }
}
