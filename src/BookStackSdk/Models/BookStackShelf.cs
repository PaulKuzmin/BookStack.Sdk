namespace BookStackSdk.Models;

/// <summary>
/// Полка BookStack: набор книг. Верхний уровень дерева содержимого.
/// </summary>
/// <remarks>
/// Полка это НЕ владелец книги, а группировка: одна и та же книга может стоять на нескольких
/// полках, а может не стоять ни на одной и при этом прекрасно существовать. Поэтому удаление полки
/// книги не трогает.
/// <para>
/// ВАЖНО про имена: маршруты называются <c>/api/shelves</c>, а поле <c>type</c> в ответе приходит
/// как <c>bookshelf</c> (замерено). В исходниках сущность тоже зовётся <c>Bookshelf</c>. Три разных
/// написания одного и того же, и при сравнении типа со строкой надо помнить именно про
/// <c>bookshelf</c>.
/// </para>
/// </remarks>
public sealed class BookStackShelf
{
    /// <summary>Идентификатор полки.</summary>
    public int? Id { get; set; }

    /// <summary>Вид сущности. Приходит как <c>bookshelf</c>, а не <c>shelf</c>. В списке этого поля нет.</summary>
    public string? Type { get; set; }

    /// <summary>Название полки.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Слаг полки, часть адреса. Пересчитывается при переименовании, см.
    /// <see cref="BookStackPage.Slug"/>.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>Описание полки простым текстом.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Описание полки в HTML. При записи спорит с <see cref="Description"/> и выигрывает, см.
    /// <see cref="BookStackBook.DescriptionHtml"/>.
    /// </summary>
    public string? DescriptionHtml { get; set; }

    /// <summary>Когда полка создана. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда полка изменена. UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Кто создал. Форма ответа двоякая (число или объект) и зависит от маршрута, разбор замеров
    /// в <see cref="BookStackPage.CreatedBy"/>.
    /// </summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто изменил последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Кто владеет полкой.</summary>
    public BookStackUserRef? OwnedBy { get; set; }

    /// <summary>
    /// Обложка полки.
    /// </summary>
    /// <remarks>
    /// Поля <c>image_id</c> у полки, в отличие от книги, нет ни в одном ответе: модель
    /// <c>app/Entities/Models/Bookshelf.php</c> объявляет его в <c>$hidden</c>. Поэтому узнать,
    /// стоит ли обложка, можно только по этому полю, и только там, где оно приходит.
    /// Про то, чем запись обложки в списке отличается от записи при чтении, см.
    /// <see cref="BookStackBook.Cover"/>. У полки вид картинки называется <c>cover_bookshelf</c>.
    /// </remarks>
    public BookStackImage? Cover { get; set; }

    /// <summary>
    /// Книги полки в том порядке, в каком они на ней стоят. Приходит только при чтении одиночной полки.
    /// </summary>
    /// <remarks>
    /// Книги приходят почти целиком (вплоть до <c>image_id</c> и <c>sort_rule_id</c>), но без
    /// тегов, обложки и оглавления. Ссылки на пользователей внутри этого набора приходят числами,
    /// см. <see cref="BookStackUserRef"/>. Список ограничен видимыми текущему токену книгами.
    /// <para>
    /// ВАЖНО про запись: при обновлении полки присланный список книг ЗАМЕЩАЕТ прежний целиком, а не
    /// дополняет его, и порядок в списке становится порядком на полке. Так написано в живой доке
    /// маршрута <c>shelves-update</c>, и это единственный способ поменять состав полки через API.
    /// </para>
    /// </remarks>
    public List<BookStackBook>? Books { get; set; }

    /// <summary>Теги полки. Приходят при чтении, создании и обновлении, в списке их нет.</summary>
    public List<BookStackTag>? Tags { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"полка #{Id} {Name}";
}
