using Marten;
using Marten.Newtonsoft;
using Marten.Services;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Shared.Infrastructure.CurrentTenancyProvider;
using Shared.Infrastructure.Persistence.Marten.Repository.Document;
using Shared.Infrastructure.Persistence.Marten.SessionFactory;
using Shared.Infrastructure.Persistence.Repositories;
using Weasel.Core;

namespace Shared.Infrastructure.Persistence.Marten.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddMartenPostgresPersistence(
        this IServiceCollection services,
        string connectionString,
        Action<StoreOptions>? configureStore = null,
        Action<JsonSerializerSettings>? configureSerialization = null)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var serializer = new JsonNetSerializer
        {
            EnumStorage = EnumStorage.AsString
        };
        serializer.Configure(settings =>
        {
            settings.TypeNameHandling = TypeNameHandling.Objects;
            settings.DateTimeZoneHandling = DateTimeZoneHandling.Unspecified;
            configureSerialization?.Invoke(settings);
        });

        services.AddNpgsqlDataSource(connectionString);
        var configuration = services.AddMarten(_ =>
        {
            var options = new StoreOptions();
            options.Serializer(serializer);
            configureStore?.Invoke(options);
            return options;
        });
        configuration.UseNpgsqlDataSource();
        configuration.ApplyAllDatabaseChangesOnStartup();
        configuration.AssertDatabaseMatchesConfigurationOnStartup();

        services.AddSingleton<ICurrentTenancyProvider, CurrentTenancyProviderNoTenancy>();
        services.AddTransient<INoTenancyByDefaultSessionFactory, NoTenancyByDefaultSessionFactory>();
        services.AddTransient<ISessionFactoryWithAlternateTenantSettings, SessionFactoryWithAlternateTenantSettings>();
        services.AddTransient<IAsyncSessionFactory, AppMartenSessionFactory>();
        services.AddScoped(typeof(IRepository<>), typeof(NoTenancyMartenRepository<>));
        services.AddScoped(typeof(IReadOnlyRepository<>), typeof(NoTenancyMartenRepository<>));
        services.AddScoped(typeof(INoTenancyRepository<>), typeof(NoTenancyMartenRepository<>));
        services.AddScoped(typeof(INoTenancyReadOnlyRepository<>), typeof(NoTenancyMartenRepository<>));
        return services;
    }
}
