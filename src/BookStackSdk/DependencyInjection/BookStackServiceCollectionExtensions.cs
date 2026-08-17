using BookStackSdk.Abstractions;
using BookStackSdk.Api;
using BookStackSdk.Auth;
using BookStackSdk.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BookStackSdk.DependencyInjection;

/// <summary>Регистрация клиента BookStack REST API в контейнере зависимостей.</summary>
public static class BookStackServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует клиент, читая настройки из секции конфигурации
    /// (например, <c>Configuration.GetSection("BookStack")</c>).
    /// </summary>
    public static IServiceCollection AddBookStack(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return services.AddBookStack(configuration.Bind);
    }

    /// <summary>Регистрирует настройки, провайдер токена и типизированные клиенты API.</summary>
    public static IServiceCollection AddBookStack(this IServiceCollection services, Action<BookStackOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<BookStackOptions>()
            .Configure(configure)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IBookStackTokenProvider, BookStackOptionsTokenProvider>();
        services.AddTransient<BookStackAuthHandler>();
        services.AddTransient<BookStackRetryHandler>();

        AddApi<IBookStackContentApi, BookStackContentApi>(services);
        AddApi<IBookStackUploadsApi, BookStackUploadsApi>(services);
        AddApi<IBookStackSearchApi, BookStackSearchApi>(services);
        AddApi<IBookStackUsersApi, BookStackUsersApi>(services);
        AddApi<IBookStackRolesApi, BookStackRolesApi>(services);
        AddApi<IBookStackRecycleBinApi, BookStackRecycleBinApi>(services);
        AddApi<IBookStackSystemApi, BookStackSystemApi>(services);
        AddApi<IBookStackExportApi, BookStackExportApi>(services);

        return services;
    }

    private static void AddApi<TInterface, TImplementation>(IServiceCollection services)
        where TInterface : class
        where TImplementation : class, TInterface
    {
        services.AddHttpClient<TInterface, TImplementation>((sp, http) =>
            {
                var opt = sp.GetRequiredService<IOptions<BookStackOptions>>().Value;
                http.BaseAddress = new Uri(opt.ResolveApiBaseUrl());
                http.Timeout = opt.Timeout;
            })
            // Порядок важен и закреплён тестом: снаружи повтор транспорта, внутри подстановка
            // токена. Обратный порядок означал бы, что повторный запрос уходит без заголовка.
            .AddHttpMessageHandler<BookStackRetryHandler>()
            .AddHttpMessageHandler<BookStackAuthHandler>();
    }
}
