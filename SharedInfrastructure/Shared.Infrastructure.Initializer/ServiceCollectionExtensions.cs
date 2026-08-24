using Microsoft.Extensions.DependencyInjection;

namespace Shared.Infrastructure.Initializer;

public static class ServiceCollectionExtensions
{
    public static void AddAllInitializers(
        this IServiceCollection services,
        string applicationAssemblyPrefix)
    {
        if (string.IsNullOrWhiteSpace(applicationAssemblyPrefix))
        {
            throw new ArgumentException(
                "An application assembly prefix is required.",
                nameof(applicationAssemblyPrefix));
        }

        var initializerType = typeof(IInitializer);
        var assemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly =>
                assembly.GetName().Name?.StartsWith(applicationAssemblyPrefix, StringComparison.Ordinal) is true ||
                assembly.GetName().Name?.StartsWith("Shared.Infrastructure.", StringComparison.Ordinal) is true)
            .OrderBy(assembly => assembly.FullName, StringComparer.Ordinal);

        foreach (var assembly in assemblies)
        {
            foreach (var type in assembly.GetTypes()
                         .Where(type =>
                             initializerType.IsAssignableFrom(type) &&
                             type is { IsClass: true, IsAbstract: false })
                         .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                services.AddTransient(initializerType, type);
            }
        }
    }
}
