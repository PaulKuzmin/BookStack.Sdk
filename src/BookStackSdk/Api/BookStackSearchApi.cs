using System.Globalization;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using BookStackSdk.Search;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackSearchApi"/>
public sealed class BookStackSearchApi : BookStackApiBase, IBookStackSearchApi
{
    public BookStackSearchApi(HttpClient http, ILogger<BookStackSearchApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackSearchResult>> SearchAsync(
        string query, int? page = null, int? count = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        // Строка запроса уходит одним параметром и кодируется целиком. Внутреннее устройство
        // (кавычки, скобки, дефисы отрицания) для транспорта значения не имеет: разбирать её будет
        // сервер, а собирать должен BookStackSearchQuery.
        //
        // Параметр ставится ЯВНО, а не через общий сборщик, который пропускает пустые значения:
        // пустой запрос должен уехать пустым и получить в ответ 422 «query обязателен» (замерено).
        // Молча выброшенный параметр превратил бы понятный отказ сервера в другой отказ, разбирать
        // который пришлось бы дольше.
        var queryString = "?query=" + Uri.EscapeDataString(query)
            + (page is null ? string.Empty : "&page=" + page.Value.ToString(CultureInfo.InvariantCulture))
            + (count is null ? string.Empty : "&count=" + count.Value.ToString(CultureInfo.InvariantCulture));

        var raw = await GetRawAsync("search" + queryString, ct).ConfigureAwait(false);

        // Конверт тот же, что у списков: {"data": [...], "total": N}. Но "total" тут это число
        // найденного ВСЕГО, а не размер страницы, и оно же ограничено потолком выдачи поиска.
        return UnwrapPage<BookStackSearchResult>(raw.Body);
    }

    /// <inheritdoc />
    public Task<BookStackPage<BookStackSearchResult>> SearchAsync(
        BookStackSearchQuery query, int? page = null, int? count = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        return SearchAsync(query.Build(), page, count, ct);
    }
}
