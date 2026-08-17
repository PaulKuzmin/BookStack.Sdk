using System.Globalization;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackRecycleBinApi"/>
public sealed class BookStackRecycleBinApi : BookStackApiBase, IBookStackRecycleBinApi
{
    public BookStackRecycleBinApi(HttpClient http, ILogger<BookStackRecycleBinApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackRecycleBinItem>> ListAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        CancellationToken ct = default)
    {
        var query = BookStackQuery.Build(
            ("count", count?.ToString(CultureInfo.InvariantCulture)),
            ("offset", offset?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort));

        var raw = await GetRawAsync("recycle-bin" + query, ct).ConfigureAwait(false);
        return UnwrapPage<BookStackRecycleBinItem>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackRecycleBinRestoreResult?> RestoreAsync(
        int deletionId, CancellationToken ct = default)
    {
        // Своих полей у восстановления нет, но метод именно PUT: так объявлен маршрут в живой
        // доке. Пустой объект уходит телом, потому что общий PutRawAsync собирает тело всегда,
        // и это проверено вживую: и запрос вовсе без тела, и запрос с телом {} отвечают 200
        // с одинаковым restore_count.
        var raw = await PutRawAsync($"recycle-bin/{deletionId}", new { }, ct).ConfigureAwait(false);
        return Deserialize<BookStackRecycleBinRestoreResult>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackRecycleBinDestroyResult?> DestroyAsync(
        int deletionId, CancellationToken ct = default)
    {
        var raw = await DeleteRawAsync($"recycle-bin/{deletionId}", ct).ConfigureAwait(false);

        // Единственное удаление во всём API, которое отвечает не пустотой, а телом: тут приходит
        // {"delete_count": N}. Прочие DELETE отдают 204 без содержимого (замерено).
        return Deserialize<BookStackRecycleBinDestroyResult>(raw.Body);
    }
}
