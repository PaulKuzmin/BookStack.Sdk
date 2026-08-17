using System.Net;
using System.Text.RegularExpressions;
using BookStackSdk.Auth;
using BookStackSdk.Http;
using BookStackSdk.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на заголовок <c>Authorization</c>.
/// </summary>
/// <remarks>
/// Проверять тут нечего, кроме одной строки, и именно поэтому проверок так много. Замерено на
/// стенде 17.08.2026 одним и тем же живым токеном: <c>Token {id}:{secret}</c> даёт 200,
/// <c>Bearer {id}:{secret}</c> даёт 401, а <c>Token {secret}:{id}</c> (половинки местами) даёт 401
/// с текстом «No matching API token was found for the provided authorization token», то есть ровно
/// тем же, что и на просроченный, отозванный или чужой токен. Ответ не подсказывает, что перепутан
/// порядок, а не сам токен, поэтому единственное место, где перестановку можно поймать, это тест.
/// </remarks>
public class AuthHeaderTests
{
    /// <summary>Половинки нарочно различимы на глаз: перестановка обязана быть видна в тексте отказа теста.</summary>
    private const string TokenId = "IdIdIdIdIdIdIdIdIdIdIdIdIdIdIdId";

    private const string TokenSecret = "SecretSecretSecretSecretSecretSe";

    [Fact]
    public async Task Header_IsExactlyTokenIdColonSecret()
    {
        var stub = StubHttpMessageHandler.Json("{}");
        var client = BuildClient(stub, out _);

        await client.GetAsync("system");

        // Сравнение с полной строкой, а не с кусками: любая правка сборки заголовка (другая схема,
        // другой разделитель, переставленные половинки) валит именно этот тест.
        stub.Requests.Single().Authorization.Should().Be($"Token {TokenId}:{TokenSecret}");
    }

    [Fact]
    public void Header_HalvesAreNotSwapped()
    {
        var options = new BookStackOptions { BaseUrl = ApiFactory.BaseUrl, TokenId = TokenId, TokenSecret = TokenSecret };

        var header = options.BuildAuthorizationHeaderValue();

        // Позиционная проверка, а не сравнение целиком: она называет, какая половинка где стоит,
        // и падение читается сразу, без сличения двух похожих строк по 32 символа.
        var value = header["Token ".Length..];
        var halves = value.Split(':');
        halves.Should().HaveCount(2, "разделитель половинок ровно один, двоеточие");
        halves[0].Should().Be(TokenId, "первой идёт половинка-идентификатор");
        halves[1].Should().Be(TokenSecret, "второй идёт половинка-секрет");

        // И прямой замок на подмену: перевёрнутый вариант это тот самый 401, который не отличить
        // от негодного токена.
        header.Should().NotBe($"Token {TokenSecret}:{TokenId}");
    }

    [Fact]
    public async Task Header_SwappedHalves_ChangeWhatGoesOnTheWire()
    {
        // Проверка того, что предыдущий замок вообще способен что-то поймать: настройки с
        // переставленными половинками обязаны дать другую строку на проводе, а не ту же самую.
        var stub = StubHttpMessageHandler.Json("{}");
        var swapped = new BookStackOptions
        {
            BaseUrl = ApiFactory.BaseUrl,
            TokenId = TokenSecret,
            TokenSecret = TokenId,
        };
        var handler = new BookStackAuthHandler(
            new BookStackOptionsTokenProvider(new MutableOptionsMonitor(swapped)))
        {
            InnerHandler = stub,
        };

        await ApiFactory.CreateClient(handler).GetAsync("system");

        stub.Requests.Single().Authorization
            .Should().NotBe($"Token {TokenId}:{TokenSecret}")
            .And.Be($"Token {TokenSecret}:{TokenId}");
    }

    [Fact]
    public async Task Header_UsesTokenScheme_NotBearer()
    {
        var stub = StubHttpMessageHandler.Json("{}");
        var client = BuildClient(stub, out _);

        await client.GetAsync("system");

        var header = stub.Requests.Single().Authorization!;
        header.Should().StartWith("Token ");
        header.Should().NotStartWith("Bearer", "замерено: Bearer с тем же токеном даёт 401");
    }

    [Fact]
    public async Task Header_HasNoStrayWhitespace()
    {
        var stub = StubHttpMessageHandler.Json("{}");
        var client = BuildClient(stub, out _);

        await client.GetAsync("system");

        var header = stub.Requests.Single().Authorization!;

        // Пробел ровно один, после схемы. Двоеточие ровно одно. Ни ведущих, ни хвостовых пробелов:
        // токен половинками сравнивается сервером посимвольно, и лишний пробел это тот же 401.
        Regex.IsMatch(header, @"^Token [^\s:]+:[^\s:]+$").Should().BeTrue($"заголовок пришёл как «{header}»");
        header.Should().Be(header.Trim());
    }

    [Fact]
    public async Task Header_SetByCaller_IsNotOverwritten()
    {
        // Путь для проб под чужим токеном: если заголовок уже стоит, обработчик его не трогает.
        var stub = StubHttpMessageHandler.Json("{}");
        var client = BuildClient(stub, out _);

        using var request = new HttpRequestMessage(HttpMethod.Get, "system");
        request.Headers.TryAddWithoutValidation("Authorization", "Token other:token");
        await client.SendAsync(request);

        stub.Requests.Single().Authorization.Should().Be("Token other:token");
    }

    [Fact]
    public async Task Header_IsRebuiltOnEachRequest_AfterRotation()
    {
        // Токен BookStack ротируют, не пересобирая клиента: значение обязано читаться из настроек
        // на каждом запросе, а не запоминаться при сборке цепочки.
        var stub = StubHttpMessageHandler.Json("{}");
        var client = BuildClient(stub, out var monitor);

        await client.GetAsync("system");

        monitor.CurrentValue = new BookStackOptions
        {
            BaseUrl = ApiFactory.BaseUrl,
            TokenId = "NewIdNewIdNewIdNewIdNewIdNewIdNe",
            TokenSecret = "NewSecretNewSecretNewSecretNewSe",
        };
        await client.GetAsync("system");

        stub.Requests[0].Authorization.Should().Be($"Token {TokenId}:{TokenSecret}");
        stub.Requests[1].Authorization.Should().Be("Token NewIdNewIdNewIdNewIdNewIdNewIdNe:NewSecretNewSecretNewSecretNewSe");
    }

    [Fact]
    public async Task Header_SurvivesRetryClone()
    {
        // Обработчик повторов пересобирает запрос копией, и заголовки переносит он сам. Потеря
        // заголовка при повторе выглядела бы как 401 после 429, то есть как отозванный токен.
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            attempt++;
            return StubHttpMessageHandler.JsonResponse(
                "{}", attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);
        });

        var clock = new FakeTimeProvider();
        var retry = new BookStackRetryHandler(
            Options.Create(new BookStackOptions { BaseUrl = ApiFactory.BaseUrl, MaxRetryAttempts = 1 }),
            NullLogger<BookStackRetryHandler>.Instance,
            clock)
        {
            InnerHandler = stub,
        };
        var auth = new BookStackAuthHandler(
            new BookStackOptionsTokenProvider(new MutableOptionsMonitor(NewOptions())))
        {
            InnerHandler = retry,
        };

        var send = ApiFactory.CreateClient(auth).GetAsync("system");
        await DrainAsync(send, clock);

        stub.Requests.Should().HaveCount(2);
        stub.Requests.Should().AllSatisfy(r => r.Authorization.Should().Be($"Token {TokenId}:{TokenSecret}"));
    }

    private static HttpClient BuildClient(StubHttpMessageHandler stub, out MutableOptionsMonitor monitor)
    {
        monitor = new MutableOptionsMonitor(NewOptions());
        var handler = new BookStackAuthHandler(new BookStackOptionsTokenProvider(monitor)) { InnerHandler = stub };
        return ApiFactory.CreateClient(handler);
    }

    private static BookStackOptions NewOptions() => new()
    {
        BaseUrl = ApiFactory.BaseUrl,
        TokenId = TokenId,
        TokenSecret = TokenSecret,
    };

    /// <summary>Прокручивает время, пока запрос ждёт паузы перед повтором.</summary>
    private static async Task DrainAsync(Task<HttpResponseMessage> send, FakeTimeProvider clock)
    {
        while (!send.IsCompleted)
        {
            await Task.Delay(1);
            clock.Advance(TimeSpan.FromSeconds(10));
        }

        (await send).Dispose();
    }

    /// <summary>Настройки, которые можно подменить между запросами: так ротируют токен.</summary>
    private sealed class MutableOptionsMonitor : IOptionsMonitor<BookStackOptions>
    {
        public MutableOptionsMonitor(BookStackOptions value) => CurrentValue = value;

        public BookStackOptions CurrentValue { get; set; }

        public BookStackOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<BookStackOptions, string?> listener) => null;
    }
}
