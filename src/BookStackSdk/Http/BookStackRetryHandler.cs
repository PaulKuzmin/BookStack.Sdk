using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BookStackSdk.Http;

/// <summary>
/// Повторяет запрос при перегрузке (<c>429</c>), временных ошибках сервера (<c>5xx</c>) и сетевых
/// сбоях. Паузу диктует сервер, если он её назвал, иначе она считается экспоненциально.
/// </summary>
/// <remarks>
/// Решение о повторе строится на СТАТУСЕ и ИДЕМПОТЕНТНОСТИ МЕТОДА, а не на тексте ошибки: по тексту
/// не отличить вечно непроходимое тело от разового сбоя, и гадание на строке даёт то бесконечные
/// повторы безнадёжного, то отказ от повтора того, что прошло бы со второго раза.
/// <para>
/// Отсюда правило: <c>429</c> повторяем всегда, <c>5xx</c> и сетевой сбой только у идемпотентных
/// методов. Создание страницы или книги повторять нельзя: ключа идемпотентности BookStack не даёт,
/// и второй заход после «упало на нашей стороне сети» оставил бы дубль.
/// </para>
/// <para>
/// ВАЖНО про обновление файловых полей. Обложки и вложения уходят методом <c>POST</c> с полем
/// <c>_method=PUT</c> внутри тела (см. <see cref="Internal.BookStackMultipart"/>), то есть по сути
/// идемпотентное обновление выглядит отсюда как <c>POST</c> и на <c>5xx</c> повторено не будет.
/// Так и задумано: чтобы решить иначе, обработчику пришлось бы читать поле подмены из тела и
/// доверять ему, а цена ошибки несимметрична. Непереставленная обложка это одна ручная команда,
/// лишняя книга это чужая правка чужой документации.
/// </para>
/// </remarks>
public sealed class BookStackRetryHandler : DelegatingHandler
{
    private readonly IOptions<BookStackOptions> _options;
    private readonly ILogger<BookStackRetryHandler> _logger;
    private readonly TimeProvider _clock;

    public BookStackRetryHandler(
        IOptions<BookStackOptions> options,
        ILogger<BookStackRetryHandler> logger,
        TimeProvider clock)
    {
        _options = options;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var opt = _options.Value;
        var maxAttempts = Math.Max(0, opt.MaxRetryAttempts);
        var idempotent = IsIdempotent(request.Method);

        var body = request.Content is null
            ? null
            : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        for (var attempt = 0; ; attempt++)
        {
            HttpResponseMessage? response = null;
            Exception? transportError = null;

            using var attemptRequest = Clone(request, body);
            try
            {
                response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
                if (attempt >= maxAttempts || !ShouldRetry(response.StatusCode, idempotent))
                    return response;
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && idempotent)
            {
                transportError = ex;
            }

            var delay = GetDelay(response, attempt, opt);
            _logger.LogWarning(
                "BookStack: повтор {Attempt}/{Max} через {Delay}, причина: {Reason}",
                attempt + 1, maxAttempts, delay,
                transportError?.Message ?? $"HTTP {(int)response!.StatusCode}");

            response?.Dispose();
            await Task.Delay(delay, _clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Методы, повтор которых не может создать вторую сущность.</summary>
    private static bool IsIdempotent(HttpMethod method)
        => method == HttpMethod.Get
           || method == HttpMethod.Head
           || method == HttpMethod.Put
           || method == HttpMethod.Delete
           || method == HttpMethod.Options
           || method == HttpMethod.Trace;

    private static bool ShouldRetry(HttpStatusCode status, bool idempotent)
    {
        // Ограничитель частоты у BookStack стоит перед обработчиком маршрута: до создания сущности
        // такой запрос не доходит, поэтому повтор безопасен для любого метода. Подтверждение:
        // на 429 приходит Retry-After и X-RateLimit-Remaining: 0, а тела ответа маршрута нет.
        if (status == HttpStatusCode.TooManyRequests)
            return true;

        return (int)status >= 500 && idempotent;
    }

    /// <summary>
    /// Пауза перед повтором. Приоритет у того, что назвал сервер.
    /// </summary>
    /// <remarks>
    /// Замерено на стенде 17.08.2026 (лимит 180 запросов в минуту, выбран двумя сотнями параллельных
    /// вызовов): на <c>429</c> BookStack присылает <c>Retry-After: 31</c> и
    /// <c>X-RateLimit-Reset: 1786948802</c> (unix-секунды), плюс <c>X-RateLimit-Remaining: 0</c>.
    /// Пара <c>X-RateLimit-Limit</c> и <c>X-RateLimit-Remaining</c> приходит на КАЖДЫЙ ответ,
    /// включая 200, 401 и 422, а <c>Retry-After</c> и <c>X-RateLimit-Reset</c> только на 429.
    /// Поэтому <c>X-RateLimit-Reset</c> используется как запасной вариант, а не как основной:
    /// расчётная экспонента остаётся последней ступенью, на случай если ограничитель поменяют.
    /// </remarks>
    private TimeSpan GetDelay(HttpResponseMessage? response, int attempt, BookStackOptions opt)
    {
        var retryAfter = response?.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
            return delta;
        if (retryAfter?.Date is { } date)
        {
            var wait = date - _clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
                return wait;
        }

        if (TryGetRateLimitReset(response, out var reset))
        {
            var wait = reset - _clock.GetUtcNow();
            if (wait > TimeSpan.Zero)
                return wait;
        }

        return opt.RetryBaseDelay * Math.Pow(2, attempt);
    }

    /// <summary>Разбирает <c>X-RateLimit-Reset</c>: момент снятия ограничения в unix-секундах.</summary>
    private static bool TryGetRateLimitReset(HttpResponseMessage? response, out DateTimeOffset reset)
    {
        reset = default;

        if (response is null || !response.Headers.TryGetValues("X-RateLimit-Reset", out var values))
            return false;

        var raw = values.FirstOrDefault();
        if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            return false;

        reset = DateTimeOffset.FromUnixTimeSeconds(seconds);
        return true;
    }

    private static HttpRequestMessage Clone(HttpRequestMessage source, byte[]? body)
    {
        var clone = new HttpRequestMessage(source.Method, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy,
        };

        if (body is not null)
        {
            clone.Content = new ByteArrayContent(body);
            if (source.Content is not null)
            {
                // Заголовки содержимого копируются как есть. Для нас это в первую очередь
                // Content-Type: он несёт и charset у JSON, и boundary у multipart, и потеря
                // любого из них превращает повтор в запрос с другим смыслом.
                foreach (var header in source.Content.Headers)
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in source.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var option in source.Options)
            ((IDictionary<string, object?>)clone.Options)[option.Key] = option.Value;

        return clone;
    }
}
