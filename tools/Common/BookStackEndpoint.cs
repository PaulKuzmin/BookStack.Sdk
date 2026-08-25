using BookStackSdk.Abstractions;
using BookStackSdk.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookStackTools;

/// <summary>
/// Одна установка BookStack: свой контейнер, свои настройки, свои клиенты API.
/// </summary>
/// <remarks>
/// Контейнера ДВА, по одному на установку, и это не лень, а устройство SDK:
/// <see cref="BookStackServiceCollectionExtensions.AddBookStack(IServiceCollection, Action{BookStackOptions})"/>
/// регистрирует один набор настроек на контейнер, потому что приложению обычно нужен один портал.
/// Здесь порталов два, и разделить их адреса и токены внутри одного контейнера нечем: перепутанные
/// половинки токена дают тот же 401, что и чужой токен, а перепутанные адреса вообще не дают ошибки,
/// просто уносят запись не туда.
/// </remarks>
internal sealed class BookStackEndpoint : IDisposable
{
    private readonly ServiceProvider _provider;

    private BookStackEndpoint(ServiceProvider provider, string baseUrl)
    {
        _provider = provider;
        BaseUrl = baseUrl;

        Content = provider.GetRequiredService<IBookStackContentApi>();
        Export = provider.GetRequiredService<IBookStackExportApi>();
        Import = provider.GetRequiredService<IBookStackImportApi>();
        Uploads = provider.GetRequiredService<IBookStackUploadsApi>();
        RecycleBin = provider.GetRequiredService<IBookStackRecycleBinApi>();
        System = provider.GetRequiredService<IBookStackSystemApi>();
    }

    /// <summary>Адрес установки без хвостового слэша, как его задали в командной строке.</summary>
    public string BaseUrl { get; }

    public IBookStackContentApi Content { get; }

    public IBookStackExportApi Export { get; }

    public IBookStackImportApi Import { get; }

    public IBookStackUploadsApi Uploads { get; }

    public IBookStackRecycleBinApi RecycleBin { get; }

    public IBookStackSystemApi System { get; }

    /// <summary>
    /// Собирает клиентов установки, взяв токен из переменных окружения.
    /// </summary>
    /// <param name="baseUrl">Адрес установки.</param>
    /// <param name="timeout">Таймаут запроса. Импорт книги с картинками идёт долго, см. README.</param>
    /// <param name="verbose">Показывать ли запросы SDK.</param>
    /// <param name="prefixes">
    /// Префиксы имён переменных в порядке предпочтения: для <c>FROM</c> берутся
    /// <c>BOOKSTACK_FROM_TOKEN_ID</c> и <c>BOOKSTACK_FROM_TOKEN_SECRET</c>. Пустой префикс это
    /// <c>BOOKSTACK_TOKEN_ID</c> и <c>BOOKSTACK_TOKEN_SECRET</c>.
    /// </param>
    /// <remarks>
    /// Половинки токена берутся ПАРОЙ из одного префикса, а не по отдельности из первого попавшегося:
    /// смешать идентификатор одной установки с секретом другой значит получить 401 тем же текстом,
    /// что и на чужой токен, и искать причину не там.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Ни один префикс не дал пары.</exception>
    public static BookStackEndpoint Create(
        string baseUrl, TimeSpan timeout, bool verbose, params string[] prefixes)
    {
        var (tokenId, tokenSecret) = ReadTokenPair(prefixes);

        var services = new ServiceCollection();

        services.AddLogging(log =>
        {
            log.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Warning);
            log.AddSimpleConsole(console => console.SingleLine = true);
        });

        services.AddBookStack(options =>
        {
            options.BaseUrl = baseUrl;
            options.TokenId = tokenId;
            options.TokenSecret = tokenSecret;
            options.Timeout = timeout;
        });

        return new BookStackEndpoint(services.BuildServiceProvider(), baseUrl);
    }

    public void Dispose() => _provider.Dispose();

    /// <summary>Первая пара переменных, заданная целиком.</summary>
    private static (string Id, string Secret) ReadTokenPair(string[] prefixes)
    {
        var tried = new List<string>();

        foreach (var prefix in prefixes.Length == 0 ? [string.Empty] : prefixes)
        {
            var head = string.IsNullOrEmpty(prefix) ? "BOOKSTACK" : $"BOOKSTACK_{prefix}";
            var id = Read($"{head}_TOKEN_ID");
            var secret = Read($"{head}_TOKEN_SECRET");

            if (id is not null && secret is not null)
                return (id, secret);

            tried.Add($"{head}_TOKEN_ID + {head}_TOKEN_SECRET");
        }

        throw new InvalidOperationException(
            "Не задан токен. Нужна пара переменных окружения: " + string.Join(", либо ", tried) + ".");
    }

    private static string? Read(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
