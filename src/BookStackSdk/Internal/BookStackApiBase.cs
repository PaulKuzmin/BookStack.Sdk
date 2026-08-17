using System.Net;
using System.Text.Json;
using BookStackSdk.Errors;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Internal;

/// <summary>
/// Базовый класс API-сервисов: отправка на общий типизированный <see cref="HttpClient"/>, единый
/// разбор конверта списка и единый разбор ошибок BookStack.
/// </summary>
public abstract class BookStackApiBase
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;

    protected BookStackApiBase(HttpClient http, ILogger logger)
    {
        _http = http;
        _logger = logger;
    }

    /// <summary>Тело ответа вместе со статусом.</summary>
    /// <remarks>
    /// Метки версии (<c>ETag</c>) здесь нет намеренно: в отличие от MantisBT, BookStack условных
    /// изменений по <c>If-Match</c> не предлагает, и заводить поле под то, чего API не отдаёт,
    /// значит обещать защиту от гонки, которой нет.
    /// </remarks>
    protected readonly record struct RawResponse(string Body, HttpStatusCode Status);

    // ---- Транспорт ----

    protected Task<RawResponse> GetRawAsync(string relativeUrl, CancellationToken ct)
        => SendRawAsync(new HttpRequestMessage(HttpMethod.Get, relativeUrl), ct);

    protected Task<RawResponse> PostRawAsync<TBody>(string relativeUrl, TBody body, CancellationToken ct)
        => SendRawAsync(
            new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = BookStackJson.CreateContent(body) },
            ct);

    /// <summary>
    /// Обновление сущности телом JSON.
    /// </summary>
    /// <remarks>
    /// Тут именно <c>PUT</c>, без подмены метода: для JSON она не нужна и не нужна была никогда.
    /// Подмена лечит только пропажу ФАЙЛОВ при <c>PUT</c> на старом PHP, см.
    /// <see cref="BookStackMultipart"/>. <c>PATCH</c> у BookStack нет вовсе: в живой доке
    /// <c>/api/docs.json</c> все изменяющие маршруты объявлены как <c>PUT</c>, и частичность
    /// достигается тем, что неупомянутые поля просто не трогаются.
    /// </remarks>
    protected Task<RawResponse> PutRawAsync<TBody>(string relativeUrl, TBody body, CancellationToken ct)
        => SendRawAsync(
            new HttpRequestMessage(HttpMethod.Put, relativeUrl) { Content = BookStackJson.CreateContent(body) },
            ct);

    protected Task<RawResponse> DeleteRawAsync(string relativeUrl, CancellationToken ct)
        => SendRawAsync(new HttpRequestMessage(HttpMethod.Delete, relativeUrl), ct);

    /// <summary>
    /// Отправка многочастного тела.
    /// </summary>
    /// <remarks>
    /// Метод всегда <c>POST</c>, и для создания, и для обновления. Разница между ними живёт внутри
    /// тела, в поле <c>_method</c>, которое ставит <see cref="BookStackMultipart.ForUpdate"/>.
    /// Обоснование целиком в описании <see cref="BookStackMultipart"/>: коротко, файл при настоящем
    /// <c>PUT</c> пропадает молча на PHP старше 8.4, а ответ при этом 200.
    /// </remarks>
    protected Task<RawResponse> SendMultipartAsync(
        string relativeUrl, BookStackMultipart form, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(form);

        return SendRawAsync(
            new HttpRequestMessage(HttpMethod.Post, relativeUrl) { Content = form.Build() },
            ct);
    }

    /// <summary>
    /// Чтение двоичного содержимого: выгрузки книг, глав и страниц, а также данные картинок.
    /// </summary>
    /// <remarks>
    /// Тип содержимого и имя файла возвращаются вместе с байтами, потому что они приходят от
    /// сервера и восстановить их потом неоткуда. Замерено на стенде 17.08.2026:
    /// <c>GET /api/books/{id}/export/pdf</c> отдаёт <c>Content-Type: application/octet-stream</c>
    /// (то есть по типу содержимого PDF не опознать, тело начинается с <c>%PDF-</c>), а
    /// <c>GET /api/image-gallery/{id}/data</c> отдаёт <c>image/png</c> и
    /// <c>Content-Disposition: inline; filename=…</c>.
    /// </remarks>
    protected async Task<BookStackBinary> GetBinaryAsync(string relativeUrl, CancellationToken ct)
    {
        _logger.LogDebug("BookStack → GET (двоичное) {Url}", relativeUrl);

        using var request = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

        _logger.LogDebug("BookStack ← {Status} (двоичное) {Url}", (int)response.StatusCode, relativeUrl);

        if (!response.IsSuccessStatusCode)
        {
            // Отказ приходит обычным конвертом ошибки, то есть текстом: читаем как текст.
            var raw = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            throw BuildException(response, raw);
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);

        return new BookStackBinary(
            bytes,
            response.Content.Headers.ContentType?.MediaType,
            ReadFileName(response));
    }

    protected async Task<RawResponse> SendRawAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _logger.LogDebug("BookStack → {Method} {Url}", request.Method, request.RequestUri);

        using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        _logger.LogDebug("BookStack ← {Status} {Url}", (int)response.StatusCode, request.RequestUri);

        if (!response.IsSuccessStatusCode)
            throw BuildException(response, body);

        return new RawResponse(body, response.StatusCode);
    }

    /// <summary>Имя файла из <c>Content-Disposition</c>, если сервер его назвал.</summary>
    private static string? ReadFileName(HttpResponseMessage response)
    {
        var name = response.Content.Headers.ContentDisposition?.FileNameStar
                   ?? response.Content.Headers.ContentDisposition?.FileName;

        // Кавычки вокруг имени необязательны, и BookStack их не ставит (замерено:
        // «inline; filename=2LgCwkTVeQgfOWx5-s.png»). Снимаем, если они всё же есть.
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim('"');
    }

    /// <summary>
    /// Собирает исключение из ответа.
    /// </summary>
    /// <remarks>
    /// Конверт один на все отказы: <c>{"error": {"message": "…", "code": N}}</c>, при валидации
    /// добавляется <c>validation</c>. Проверено на 401 (без токена), 404 (чужой id), 422 (пустое
    /// тело) и 429 (выбранный лимит). Тело всё равно может оказаться не-JSON: перед приложением
    /// стоит nginx, и его собственные страницы приходят HTML-ом. Ошибка разбора не должна подменять
    /// собой ошибку вызова, поэтому разбор обёрнут, а сырое тело сохраняется как есть.
    /// </remarks>
    private static BookStackApiException BuildException(HttpResponseMessage response, string body)
    {
        BookStackError? error = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                error = JsonSerializer
                    .Deserialize<BookStackErrorEnvelope>(body, BookStackJson.Options)?
                    .Error;

                if (error is not null && error.Message is null && error.Code is null && error.Validation is null)
                    error = null;
            }
            catch (JsonException)
            {
                // Не JSON: страница nginx. Тело сохраняем сырым, разбор не навязываем.
            }
        }

        return new BookStackApiException(
            response.StatusCode, error, string.IsNullOrEmpty(body) ? null : body, response.ReasonPhrase);
    }

    // ---- Конверты ----

    /// <summary>
    /// Разбирает одиночную сущность.
    /// </summary>
    /// <remarks>
    /// Обёртки у одиночных ответов нет: чтение, создание и обновление отдают объект сущности прямо
    /// в корне. Это заметное упрощение против MantisBT, где форм конверта три и они несимметричны.
    /// <para>
    /// Пустое тело это норма, а не сбой: <c>DELETE</c> отвечает 204 без содержимого (замерено).
    /// Поэтому возвращается <c>null</c>, а не бросается исключение разбора.
    /// </para>
    /// </remarks>
    protected static T? Deserialize<T>(string raw)
        where T : class
        => string.IsNullOrWhiteSpace(raw) ? null : JsonSerializer.Deserialize<T>(raw, BookStackJson.Options);

    /// <summary>
    /// Разбирает конверт списка: <c>{"data": [...], "total": N}</c>.
    /// </summary>
    /// <remarks>
    /// Форма подтверждена и живым ответом стенда (<c>{"data":[],"total":0}</c> на пустой список
    /// книг), и примером в <c>/api/docs.json</c> у маршрута <c>books-list</c>. Постраничность у
    /// BookStack своя: сколько взять и с какого места задаётся запросом (<c>count</c> и
    /// <c>offset</c>, проверено вживую), а в ответе возвращается только <c>total</c>, то есть
    /// ПОЛНОЕ число доступных записей, а не размер выданной страницы. Размер страницы вызывающий
    /// знает и так, из своего же запроса, и повторять его в модели незачем.
    /// <para>
    /// ВАЖНО: <c>Total</c> нарочно nullable. Ноль это «записей нет», отсутствие поля это «сервер его
    /// не прислал», и подставлять на месте второго первое нельзя: на таком умолчании строится
    /// решение «долистывать ли дальше», и цена ошибки тут не косметическая.
    /// </para>
    /// </remarks>
    protected static BookStackPage<T> UnwrapPage<T>(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return new BookStackPage<T>([], null);

        using var doc = JsonDocument.Parse(raw);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return new BookStackPage<T>([], null);

        IReadOnlyList<T> data = [];
        if (root.TryGetProperty("data", out var items) && items.ValueKind == JsonValueKind.Array)
            data = items.Deserialize<List<T>>(BookStackJson.Options) ?? [];

        int? total = root.TryGetProperty("total", out var totalValue)
                     && totalValue.ValueKind == JsonValueKind.Number
                     && totalValue.TryGetInt32(out var parsed)
            ? parsed
            : null;

        return new BookStackPage<T>(data, total);
    }
}

/// <summary>Страница списка: выданные записи и полное число доступных.</summary>
/// <param name="Data">Записи выданной страницы.</param>
/// <param name="Total">
/// Полное число доступных записей. <c>null</c> означает, что сервер поля не прислал, и это не то же
/// самое, что ноль.
/// </param>
public sealed record BookStackPage<T>(IReadOnlyList<T> Data, int? Total);

/// <summary>Двоичный ответ: содержимое и то, чем сервер его назвал.</summary>
/// <param name="Content">Байты ответа.</param>
/// <param name="ContentType">
/// Тип содержимого. Полагаться на него для опознания формата нельзя: выгрузки приходят как
/// <c>application/octet-stream</c> независимо от того, PDF это, markdown или zip.
/// </param>
/// <param name="FileName">Имя файла из <c>Content-Disposition</c>, если сервер его назвал.</param>
public readonly record struct BookStackBinary(byte[] Content, string? ContentType, string? FileName);
