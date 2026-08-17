using BookStackSdk.Internal;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Формат выгрузки. Значения соответствуют последнему куску адреса маршрута.
/// </summary>
/// <remarks>
/// Набор закрыт живой докой: у книги, главы и страницы ровно по пять маршрутов выгрузки, и они
/// одинаковые. Полки не выгружаются вовсе, такого маршрута нет.
/// </remarks>
public enum BookStackExportFormat
{
    /// <summary>Один самодостаточный файл HTML со встроенным оформлением.</summary>
    Html = 0,

    /// <summary>PDF. Считается сервером на лету, см. примечание к <see cref="IBookStackExportApi"/>.</summary>
    Pdf,

    /// <summary>Простой текст без разметки. Кусок адреса тут <c>plaintext</c>, слитно.</summary>
    PlainText,

    /// <summary>Markdown.</summary>
    Markdown,

    /// <summary>
    /// Архив ZIP с содержимым и вложениями: формат переноса между установками BookStack.
    /// </summary>
    Zip,
}

/// <summary>
/// Выгрузка книг, глав и страниц.
/// </summary>
/// <remarks>
/// ВАЖНО про опознание формата. Сервер отдаёт ЛЮБУЮ выгрузку с типом содержимого
/// <c>application/octet-stream</c>, независимо от того, PDF это, markdown или архив (замерено
/// 17.08.2026 по всем пяти форматам). То есть по <see cref="BookStackBinary.ContentType"/> формат
/// не определяется, и полагаться на него нельзя. Что действительно приходит от сервера, так это
/// имя файла: <c>Content-Disposition: attachment; filename*=UTF-8''proba-sdk-zagruzok.pdf</c>,
/// и по его расширению формат виден. Имя разобрано в <see cref="BookStackBinary.FileName"/>.
/// <para>
/// ВАЖНО про время. PDF считается на лету и стоит дорого даже на пустяках: замер на стенде дал
/// 455 килобайт и заметную паузу на книге с одной страницей. Умолчание таймаута в
/// <see cref="BookStackOptions.Timeout"/> для больших книг может не подойти, и это решение
/// вызывающей стороны.
/// </para>
/// <para>
/// Полки не выгружаются: маршрута нет ни в живой доке, ни в исходниках, поэтому нет и метода.
/// </para>
/// </remarks>
public interface IBookStackExportApi
{
    /// <summary>Выгружает книгу (<c>GET /api/books/{id}/export/{формат}</c>).</summary>
    Task<BookStackBinary> ExportBookAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);

    /// <summary>Выгружает главу (<c>GET /api/chapters/{id}/export/{формат}</c>).</summary>
    Task<BookStackBinary> ExportChapterAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);

    /// <summary>Выгружает страницу (<c>GET /api/pages/{id}/export/{формат}</c>).</summary>
    Task<BookStackBinary> ExportPageAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);

    /// <summary>
    /// Выгружает книгу текстом.
    /// </summary>
    /// <param name="id">Книга.</param>
    /// <param name="format">
    /// Только текстовые форматы: <see cref="BookStackExportFormat.Markdown"/>,
    /// <see cref="BookStackExportFormat.Html"/> и <see cref="BookStackExportFormat.PlainText"/>.
    /// </param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// Байты разбираются как UTF-8. Сервер кодировку НЕ называет (тип содержимого у всех выгрузок
    /// один и без параметра <c>charset</c>), но UTF-8 подтверждён замером: кириллица в markdown
    /// и в простом тексте приходит целой. Метки порядка байтов (BOM) в начале нет.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Для <see cref="BookStackExportFormat.Pdf"/> и <see cref="BookStackExportFormat.Zip"/>:
    /// это двоичные форматы, и превращать их в строку значит портить данные. Берите
    /// <see cref="ExportBookAsync"/>.
    /// </exception>
    Task<string> ExportBookAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);

    /// <summary>Выгружает главу текстом. Условия те же, что у <see cref="ExportBookAsTextAsync"/>.</summary>
    Task<string> ExportChapterAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);

    /// <summary>Выгружает страницу текстом. Условия те же, что у <see cref="ExportBookAsTextAsync"/>.</summary>
    Task<string> ExportPageAsTextAsync(
        int id, BookStackExportFormat format, CancellationToken ct = default);
}
