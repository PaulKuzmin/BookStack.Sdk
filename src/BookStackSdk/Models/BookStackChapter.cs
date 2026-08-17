namespace BookStackSdk.Models;

/// <summary>
/// Глава BookStack: раздел внутри книги, объединяющий страницы. Глава не может лежать в главе.
/// </summary>
/// <remarks>
/// Все поля nullable по той же причине, что и у остальных моделей контента: набор заполненного
/// зависит от маршрута. Замер на стенде 17.08.2026: <c>GET /api/chapters/{id}</c> отдаёт
/// <c>description_html</c>, <c>tags</c>, <c>book_slug</c> и <c>pages</c>, а <c>GET /api/chapters</c>
/// не отдаёт ничего из этого.
/// </remarks>
public sealed class BookStackChapter
{
    /// <summary>Идентификатор главы.</summary>
    public int? Id { get; set; }

    /// <summary>Вид сущности, <c>chapter</c>. В списке этого поля нет.</summary>
    public string? Type { get; set; }

    /// <summary>Название главы.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Слаг главы, часть адреса. Пересчитывается при переименовании, см.
    /// <see cref="BookStackPage.Slug"/>.
    /// </summary>
    /// <remarks>
    /// Слаг уникален в пределах книги, а не установки: замер показал, что вторая глава с тем же
    /// именем в той же книге получила слаг с суффиксом (<c>glava-zamerov-Vzh</c>). То есть
    /// одинаковые имена сервер разрешает и разводит сам, молча.
    /// </remarks>
    public string? Slug { get; set; }

    /// <summary>Книга, которой принадлежит глава.</summary>
    public int? BookId { get; set; }

    /// <summary>
    /// Слаг книги. Приходит и при чтении главы, и в списке глав. Это отличие от страницы, у которой
    /// <c>book_slug</c> есть только в списке.
    /// </summary>
    public string? BookSlug { get; set; }

    /// <summary>Позиция главы в книге. Ею задаётся порядок показа.</summary>
    public int? Priority { get; set; }

    /// <summary>Описание главы простым текстом.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Описание главы в HTML. При записи спорит с <see cref="Description"/> и выигрывает, см.
    /// <see cref="BookStackBook.DescriptionHtml"/>.
    /// </summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>Страница-шаблон, подставляемая в новые страницы этой главы.</summary>
    public int? DefaultTemplateId { get; set; }

    /// <summary>Когда глава создана. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда глава изменена. UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Кто создал. Форма ответа двоякая (число или объект) и зависит от маршрута, разбор замеров
    /// в <see cref="BookStackPage.CreatedBy"/>.
    /// </summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто изменил последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Кто владеет главой.</summary>
    public BookStackUserRef? OwnedBy { get; set; }

    /// <summary>
    /// Страницы главы. Приходит только при чтении одиночной главы.
    /// </summary>
    /// <remarks>
    /// Страницы приходят в той же укороченной форме, что и элементы списка страниц: без
    /// <c>html</c>, <c>markdown</c> и тегов, зато с <c>revision_count</c> и <c>editor</c>.
    /// В исходнике над этим набором стоит пояснение автора BookStack, что полей тут больше обычного
    /// ради обратной совместимости. Ссылки на пользователей внутри этого набора приходят числами,
    /// а не объектами, см. <see cref="BookStackUserRef"/>.
    /// <para>
    /// Список ограничен видимыми текущему токену страницами, то есть пустой список значит «видимых
    /// страниц нет», а не «глава пуста».
    /// </para>
    /// </remarks>
    public List<BookStackPage>? Pages { get; set; }

    /// <summary>Теги главы. Приходят при чтении, создании и обновлении, в списке их нет.</summary>
    public List<BookStackTag>? Tags { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"глава #{Id} {Name}";
}
