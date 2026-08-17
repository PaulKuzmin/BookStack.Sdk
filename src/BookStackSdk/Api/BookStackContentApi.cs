using System.Runtime.CompilerServices;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackContentApi"/>
public sealed class BookStackContentApi : BookStackApiBase, IBookStackContentApi
{
    /// <summary>
    /// Размер окна инкрементального обхода, когда вызывающий его не задал.
    /// </summary>
    /// <remarks>
    /// Сто это не круглое число из головы, а умолчание самого BookStack:
    /// <c>API_DEFAULT_ITEM_COUNT</c> в <c>app/Config/api.php</c>. Совпадение намеренное, чтобы обход
    /// без параметров вёл себя ровно так же, как обычный список без параметров. Потолок там же
    /// рядом: <c>API_MAX_ITEM_COUNT</c>, по умолчанию 500, и всё, что больше, сервер молча
    /// подрезает, см. <see cref="BookStackListQuery.Count"/>.
    /// </remarks>
    private const int DefaultEnumerationWindow = 100;

    public BookStackContentApi(HttpClient http, ILogger<BookStackContentApi> logger)
        : base(http, logger)
    {
    }

    // ---- Страницы ----

    /// <inheritdoc />
    public Task<BookStackListResult<BookStackPage>> ListPagesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => ListAsync<BookStackPage>("pages", query, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<BookStackPage> EnumeratePagesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => EnumerateAsync<BookStackPage>("pages", query, ct);

    /// <inheritdoc />
    public async Task<BookStackPage?> GetPageAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        var raw = await GetRawAsync($"pages/{id}", ct).ConfigureAwait(false);
        return Deserialize<BookStackPage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackPage?> CreatePageAsync(BookStackPageCreate page, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(page);

        var raw = await PostRawAsync("pages", page, ct).ConfigureAwait(false);
        return Deserialize<BookStackPage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackPage?> UpdatePageAsync(
        int id, BookStackPageUpdate page, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentNullException.ThrowIfNull(page);

        var raw = await PutRawAsync($"pages/{id}", page, ct).ConfigureAwait(false);
        return Deserialize<BookStackPage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeletePageAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        _ = await DeleteRawAsync($"pages/{id}", ct).ConfigureAwait(false);
    }

    // ---- Книги ----

    /// <inheritdoc />
    public Task<BookStackListResult<BookStackBook>> ListBooksAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => ListAsync<BookStackBook>("books", query, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<BookStackBook> EnumerateBooksAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => EnumerateAsync<BookStackBook>("books", query, ct);

    /// <inheritdoc />
    public async Task<BookStackBook?> GetBookAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        var raw = await GetRawAsync($"books/{id}", ct).ConfigureAwait(false);
        return Deserialize<BookStackBook>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackBook?> CreateBookAsync(BookStackBookCreate book, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(book);

        var raw = await PostRawAsync("books", book, ct).ConfigureAwait(false);
        return Deserialize<BookStackBook>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackBook?> UpdateBookAsync(
        int id, BookStackBookCreate book, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentNullException.ThrowIfNull(book);

        var raw = await PutRawAsync($"books/{id}", book, ct).ConfigureAwait(false);
        return Deserialize<BookStackBook>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteBookAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        _ = await DeleteRawAsync($"books/{id}", ct).ConfigureAwait(false);
    }

    // ---- Главы ----

    /// <inheritdoc />
    public Task<BookStackListResult<BookStackChapter>> ListChaptersAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => ListAsync<BookStackChapter>("chapters", query, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<BookStackChapter> EnumerateChaptersAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => EnumerateAsync<BookStackChapter>("chapters", query, ct);

    /// <inheritdoc />
    public async Task<BookStackChapter?> GetChapterAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        var raw = await GetRawAsync($"chapters/{id}", ct).ConfigureAwait(false);
        return Deserialize<BookStackChapter>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackChapter?> CreateChapterAsync(
        BookStackChapterCreate chapter, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(chapter);

        var raw = await PostRawAsync("chapters", chapter, ct).ConfigureAwait(false);
        return Deserialize<BookStackChapter>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackChapter?> UpdateChapterAsync(
        int id, BookStackChapterCreate chapter, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentNullException.ThrowIfNull(chapter);

        var raw = await PutRawAsync($"chapters/{id}", chapter, ct).ConfigureAwait(false);
        return Deserialize<BookStackChapter>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteChapterAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        _ = await DeleteRawAsync($"chapters/{id}", ct).ConfigureAwait(false);
    }

    // ---- Полки ----

    /// <inheritdoc />
    public Task<BookStackListResult<BookStackShelf>> ListShelvesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => ListAsync<BookStackShelf>("shelves", query, ct);

    /// <inheritdoc />
    public IAsyncEnumerable<BookStackShelf> EnumerateShelvesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default)
        => EnumerateAsync<BookStackShelf>("shelves", query, ct);

    /// <inheritdoc />
    public async Task<BookStackShelf?> GetShelfAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        var raw = await GetRawAsync($"shelves/{id}", ct).ConfigureAwait(false);
        return Deserialize<BookStackShelf>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackShelf?> CreateShelfAsync(BookStackShelfCreate shelf, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shelf);

        var raw = await PostRawAsync("shelves", shelf, ct).ConfigureAwait(false);
        return Deserialize<BookStackShelf>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackShelf?> UpdateShelfAsync(
        int id, BookStackShelfCreate shelf, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);
        ArgumentNullException.ThrowIfNull(shelf);

        var raw = await PutRawAsync($"shelves/{id}", shelf, ct).ConfigureAwait(false);
        return Deserialize<BookStackShelf>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteShelfAsync(int id, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(id);

        _ = await DeleteRawAsync($"shelves/{id}", ct).ConfigureAwait(false);
    }

    // ---- Общая механика списков ----

    /// <summary>
    /// Одно окно списочного маршрута.
    /// </summary>
    /// <remarks>
    /// Все четыре списка обслуживаются одним и тем же кодом на стороне BookStack, поэтому и здесь
    /// он один. Смещение и размер окна кладутся в ответ такими, какими их ПРОСИЛИ: сервер их не
    /// повторяет, а знать, что именно спрашивали, нужно тому, кто разбирает несовпадение длины
    /// выдачи с ожиданием (сервер молча подрезает окно к отрезку от 1 до 500).
    /// </remarks>
    private async Task<BookStackListResult<T>> ListAsync<T>(
        string relativeUrl, BookStackListQuery? query, CancellationToken ct)
    {
        var effective = query ?? new BookStackListQuery();

        var raw = await GetRawAsync(relativeUrl + effective.ToQueryString(), ct).ConfigureAwait(false);
        var window = UnwrapPage<T>(raw.Body);

        return new BookStackListResult<T>(window.Data, window.Total, effective.Offset, effective.Count);
    }

    /// <summary>
    /// Инкрементальный обход списка: сам двигает смещение, пока записи не кончатся.
    /// </summary>
    /// <remarks>
    /// Постраничность у BookStack СМЕЩЕНИЕМ, а не курсором, и это накладывает ограничения, о которых
    /// вызывающий должен знать:
    /// <list type="bullet">
    /// <item>обход не атомарен. Если во время обхода кто-то создаст или удалит запись, попадающую в
    /// уже пройденную часть выборки, следующее окно сдвинется, и запись либо придёт дважды, либо не
    /// придёт вовсе. Курсора, который бы это лечил, у BookStack нет: в
    /// <c>ListingResponseBuilder::countAndOffsetQuery()</c> ровно <c>skip()</c> и <c>take()</c>;</item>
    /// <item>для выборки «что изменилось» сортировать надо по <c>updated_at</c> по ВОЗРАСТАНИЮ и
    /// брать фильтр <see cref="BookStackListQuery.UpdatedAfter"/>. При такой сортировке правка
    /// уже пройденной записи переносит её в хвост, то есть она придёт второй раз, а не потеряется.
    /// Обратный порядок теряет записи, и это худший из двух исходов;</item>
    /// <item>обход останавливается по трём признакам: пустое окно, окно короче запрошенного, или
    /// достигнутое полное число записей. Третий признак работает только если сервер прислал
    /// <c>total</c>, поэтому он не единственный.</item>
    /// </list>
    /// <para>
    /// Размер окна берётся из <see cref="BookStackListQuery.Count"/>, а если он не задан или
    /// неположителен, применяется <see cref="DefaultEnumerationWindow"/>. Неположительное окно
    /// пропускать на сервер нельзя: он молча приведёт его к единице, обход не сломается, но
    /// превратится в перебор по одной записи с запросом на каждую.
    /// </para>
    /// </remarks>
    private async IAsyncEnumerable<T> EnumerateAsync<T>(
        string relativeUrl,
        BookStackListQuery? query,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var effective = query ?? new BookStackListQuery();
        var window = effective.Count is > 0 ? effective.Count.Value : DefaultEnumerationWindow;
        var offset = effective.Offset is > 0 ? effective.Offset.Value : 0;

        while (true)
        {
            var result = await ListAsync<T>(relativeUrl, effective.WithWindow(offset, window), ct)
                .ConfigureAwait(false);

            if (result.Data.Count == 0)
                yield break;

            foreach (var item in result.Data)
                yield return item;

            offset += result.Data.Count;

            // Неполное окно означает конец выборки: сервер отдаёт ровно столько, сколько попросили,
            // пока записи есть (проверено вживую на count=1 со смещением).
            if (result.Data.Count < window)
                yield break;

            if (result.Total is { } total && offset >= total)
                yield break;
        }
    }
}
