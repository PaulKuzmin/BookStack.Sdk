namespace BookStackSdk.Models;

/// <summary>
/// Тело создания и обновления главы (<c>POST /api/chapters</c> и <c>PUT /api/chapters/{id}</c>).
/// </summary>
/// <remarks>
/// Тип один на оба действия по той же причине, что и у книги: наборы полей совпадают, разнится
/// только строгость (<c>book_id</c> и <c>name</c> обязательны на создании и необязательны на
/// обновлении). Сверено с исходником <c>ChapterApiController::$rules</c>.
/// </remarks>
public sealed class BookStackChapterCreate
{
    /// <summary>
    /// Книга главы. На создании обязательна.
    /// </summary>
    /// <remarks>
    /// На обновлении это ПЕРЕНОС главы в другую книгу вместе со всеми её страницами, а не просто
    /// смена поля. Так написано в живой доке маршрута <c>chapters-update</c>, и права проверяются
    /// на обе книги.
    /// </remarks>
    public int? BookId { get; set; }

    /// <summary>Название главы. На создании обязательно, ограничение длины 255 символов.</summary>
    public string? Name { get; set; }

    /// <summary>Описание простым текстом. Ограничение длины 1900 символов.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Описание в HTML. Ограничение длины 2000 символов. При отправке обоих описаний выигрывает
    /// это, см. <see cref="BookStackBookCreate.DescriptionHtml"/>.
    /// </summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>
    /// Позиция главы в книге.
    /// </summary>
    /// <remarks>
    /// Задаёт порядок показа. Если не задать при создании, сервер назначит номер сам.
    /// </remarks>
    public int? Priority { get; set; }

    /// <summary>Страница-шаблон для новых страниц этой главы.</summary>
    public int? DefaultTemplateId { get; set; }

    /// <summary>
    /// Теги главы. Три состояния те же, что и везде, см. <see cref="BookStackTag"/>.
    /// </summary>
    public List<BookStackTag>? Tags { get; set; }
}
