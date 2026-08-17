using System.Text;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackExportApi"/>
public sealed class BookStackExportApi : BookStackApiBase, IBookStackExportApi
{
    public BookStackExportApi(HttpClient http, ILogger<BookStackExportApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public Task<BookStackBinary> ExportBookAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => GetBinaryAsync($"books/{id}/export/{ToSegment(format)}", ct);

    /// <inheritdoc />
    public Task<BookStackBinary> ExportChapterAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => GetBinaryAsync($"chapters/{id}/export/{ToSegment(format)}", ct);

    /// <inheritdoc />
    public Task<BookStackBinary> ExportPageAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => GetBinaryAsync($"pages/{id}/export/{ToSegment(format)}", ct);

    /// <inheritdoc />
    public Task<string> ExportBookAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => ExportAsTextAsync(ExportBookAsync(id, EnsureTextual(format), ct));

    /// <inheritdoc />
    public Task<string> ExportChapterAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => ExportAsTextAsync(ExportChapterAsync(id, EnsureTextual(format), ct));

    /// <inheritdoc />
    public Task<string> ExportPageAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default)
        => ExportAsTextAsync(ExportPageAsync(id, EnsureTextual(format), ct));

    /// <summary>
    /// Кусок адреса для формата.
    /// </summary>
    /// <remarks>
    /// Имена взяты из живой доки дословно. Обратите внимание на простой текст: маршрут зовётся
    /// <c>plaintext</c> слитно, хотя имя маршрута в доке пишется <c>pages-export-plain-text</c>
    /// через дефисы. Ошибиться тут легко, а ответ на опечатку будет 404 без пояснений.
    /// </remarks>
    private static string ToSegment(BookStackExportFormat format) => format switch
    {
        BookStackExportFormat.Html => "html",
        BookStackExportFormat.Pdf => "pdf",
        BookStackExportFormat.PlainText => "plaintext",
        BookStackExportFormat.Markdown => "markdown",
        BookStackExportFormat.Zip => "zip",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Такого формата выгрузки нет."),
    };

    /// <summary>
    /// Отвергает двоичные форматы у текстовых методов.
    /// </summary>
    /// <remarks>
    /// Это не забота о вызывающем, а защита от молчаливой порчи: байты PDF, пропущенные через
    /// разбор UTF-8, превращаются в строку с символами замены, и потерю уже не восстановить.
    /// Отказ до вызова дешевле, чем испорченный файл после.
    /// </remarks>
    private static BookStackExportFormat EnsureTextual(BookStackExportFormat format)
        => format is BookStackExportFormat.Pdf or BookStackExportFormat.Zip
            ? throw new ArgumentOutOfRangeException(
                nameof(format), format, "Формат двоичный, текстом он не отдаётся. Берите метод, возвращающий байты.")
            : format;

    /// <summary>
    /// Разбирает выгрузку как UTF-8.
    /// </summary>
    /// <remarks>
    /// Кодировку сервер не называет: тип содержимого у всех выгрузок один и тот же
    /// (<c>application/octet-stream</c>), параметра <c>charset</c> в нём нет. UTF-8 подтверждён
    /// замером 17.08.2026: markdown и простой текст с кириллицей приходят целыми, метки порядка
    /// байтов в начале нет.
    /// </remarks>
    private static async Task<string> ExportAsTextAsync(Task<BookStackBinary> export)
    {
        var binary = await export.ConfigureAwait(false);
        return Encoding.UTF8.GetString(binary.Content);
    }
}
