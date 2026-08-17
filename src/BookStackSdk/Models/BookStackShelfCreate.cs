namespace BookStackSdk.Models;

/// <summary>
/// Тело создания и обновления полки (<c>POST /api/shelves</c> и <c>PUT /api/shelves/{id}</c>).
/// </summary>
/// <remarks>
/// Тип один на оба действия: наборы полей совпадают, разнится только обязательность имени.
/// Сверено с исходником <c>BookshelfApiController::rules()</c>.
/// <para>
/// Обложки тут, как и у книги, нет: она уходит многочастным телом с полем <c>_method=PUT</c>,
/// см. <see cref="Internal.BookStackMultipart"/>.
/// </para>
/// </remarks>
public sealed class BookStackShelfCreate
{
    /// <summary>Название полки. На создании обязательно, ограничение длины 255 символов.</summary>
    public string? Name { get; set; }

    /// <summary>Описание простым текстом. Ограничение длины 1900 символов.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Описание в HTML. Ограничение длины 2000 символов. При отправке обоих описаний выигрывает
    /// это, см. <see cref="BookStackBookCreate.DescriptionHtml"/>.
    /// </summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>
    /// Состав полки: идентификаторы книг в том порядке, в каком они должны на ней стоять.
    /// </summary>
    /// <remarks>
    /// Три состояния, и они разведены ровно так же, как у тегов, но проверяются другим местом
    /// исходника (<c>BookshelfApiController::update()</c> берёт <c>$request-&gt;input('books', null)</c>,
    /// а <c>BookshelfRepo::update()</c> трогает состав только при <c>!is_null($bookIds)</c>):
    /// <list type="bullet">
    /// <item><c>null</c> означает «состав не трогать»;</item>
    /// <item>пустой список означает «снять с полки все книги»;</item>
    /// <item>непустой список ЗАМЕЩАЕТ прежний состав целиком, а не дополняет его.</item>
    /// </list>
    /// Порядок элементов становится порядком книг на полке, так написано в живой доке маршрутов
    /// <c>shelves-create</c> и <c>shelves-update</c>.
    /// <para>
    /// Книга при снятии с полки НЕ удаляется: полка это группировка, а не владелец. Книга,
    /// не стоящая ни на одной полке, живёт как обычно.
    /// </para>
    /// </remarks>
    public List<int>? Books { get; set; }

    /// <summary>
    /// Теги полки. Три состояния те же, что и везде, см. <see cref="BookStackTag"/>.
    /// </summary>
    public List<BookStackTag>? Tags { get; set; }
}
