using Microsoft.Extensions.Logging.Abstractions;

namespace BookStackSdk.Tests.Infrastructure;

/// <summary>Собирает API-сервис поверх заглушки транспорта (конструктор: HttpClient и ILogger&lt;T&gt;).</summary>
/// <remarks>
/// Базовый адрес берётся не строкой из головы, а через <see cref="BookStackOptions.ResolveApiBaseUrl"/>:
/// адреса внутри SDK относительные (<c>books/12</c>), и потеря хвостового слэша в базе увела бы их
/// на уровень выше, в <c>/books/12</c> вместо <c>/api/books/12</c>. Так эта мелочь проверяется
/// каждым тестом, который смотрит на путь запроса.
/// </remarks>
public static class ApiFactory
{
    /// <summary>Адрес несуществующей установки: тесты сети не касаются.</summary>
    public const string BaseUrl = "http://bookstack.invalid";

    /// <summary>Клиент поверх заданной цепочки обработчиков.</summary>
    public static HttpClient CreateClient(HttpMessageHandler handler)
        => new(handler) { BaseAddress = new Uri(new BookStackOptions { BaseUrl = BaseUrl }.ResolveApiBaseUrl()) };

    /// <summary>API-сервис поверх заглушки.</summary>
    public static T Create<T>(StubHttpMessageHandler stub)
    {
        var loggerType = typeof(NullLogger<>).MakeGenericType(typeof(T));
        var logger = loggerType.GetField("Instance")!.GetValue(null);
        return (T)Activator.CreateInstance(typeof(T), CreateClient(stub), logger)!;
    }
}
