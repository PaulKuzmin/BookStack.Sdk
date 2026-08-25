using System.Globalization;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackImportApi"/>
public sealed class BookStackImportApi : BookStackApiBase, IBookStackImportApi
{
    /// <summary>Имя поля формы, которого ждёт маршрут загрузки архива.</summary>
    private const string FileField = "file";

    public BookStackImportApi(HttpClient http, ILogger<BookStackImportApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackImport>> ListAsync(
        int? count = null, int? offset = null, string? sort = null, CancellationToken ct = default)
    {
        var query = BookStackQuery.Build(
            ("count", count?.ToString(CultureInfo.InvariantCulture)),
            ("offset", offset?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort));

        var raw = await GetRawAsync("imports" + query, ct).ConfigureAwait(false);
        return UnwrapPage<BookStackImport>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackImport?> GetAsync(int id, CancellationToken ct = default)
        => Deserialize<BookStackImport>((await GetRawAsync($"imports/{id}", ct).ConfigureAwait(false)).Body);

    /// <inheritdoc />
    public async Task<BookStackImport?> UploadAsync(
        string fileName, byte[] zip, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(zip);

        // Создание, а не обновление: подмены метода тут быть не должно, маршрут ждёт честный POST.
        var form = BookStackMultipart.ForCreate()
            .AddFile(FileField, fileName, zip, "application/zip");

        var raw = await SendMultipartAsync("imports", form, ct).ConfigureAwait(false);
        return Deserialize<BookStackImport>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackBook?> RunAsBookAsync(int importId, CancellationToken ct = default)
        => Deserialize<BookStackBook>(await RunAsync(importId, parent: null, ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<BookStackChapter?> RunAsChapterAsync(
        int importId, int bookId, CancellationToken ct = default)
        => Deserialize<BookStackChapter>(
            await RunAsync(importId, (BookStackImportParent.Book, bookId), ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task<BookStackPage?> RunAsPageAsync(
        int importId, BookStackImportParent parentType, int parentId, CancellationToken ct = default)
        => Deserialize<BookStackPage>(
            await RunAsync(importId, (parentType, parentId), ct).ConfigureAwait(false));

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken ct = default)
        => await DeleteRawAsync($"imports/{id}", ct).ConfigureAwait(false);

    /// <summary>
    /// Общий запуск: маршрут один на все три вида, разнится только родитель.
    /// </summary>
    /// <remarks>
    /// У книги полей родителя в теле НЕТ ВОВСЕ, а не пусто: правило маршрута объявлено как
    /// «обязательно, если вид главы или страницы», и пустое значение оно считает заданным, то есть
    /// неверным. Пустой объект уходит потому, что имена полей складывает
    /// <see cref="Internal.BookStackJson"/> по правилу snake_case, и писать их строками руками
    /// значило бы завести второе место, где это правило может разойтись с первым.
    /// </remarks>
    private async Task<string> RunAsync(
        int importId, (BookStackImportParent Type, int Id)? parent, CancellationToken ct)
    {
        var url = $"imports/{importId}";

        object body = parent is null
            ? new { }
            : new
            {
                ParentType = parent.Value.Type == BookStackImportParent.Chapter ? "chapter" : "book",
                ParentId = parent.Value.Id,
            };

        return (await PostRawAsync(url, body, ct).ConfigureAwait(false)).Body;
    }
}
