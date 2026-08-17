namespace BookStackSdk.Models;

/// <summary>
/// Элемент выдачи поиска (<c>GET /api/search</c>).
/// </summary>
/// <remarks>
/// Выдача СМЕШАННАЯ: в одном списке лежат полки, книги, главы и страницы, и различает их поле
/// <see cref="Type"/>. Отдельного маршрута на каждый вид нет, сузить выдачу можно только фильтром
/// <c>{type:...}</c> внутри самого запроса (см.
/// <see cref="BookStackSdk.Search.BookStackSearchQuery"/>).
/// <para>
/// Набор полей у элемента зависит от вида, и это замерено 17.08.2026 на выдаче из четырёх видов
/// сразу: у полки нет <see cref="BookId"/>, у книги нет <see cref="Book"/>, у главы есть
/// <see cref="BookId"/> и <see cref="Book"/>, но нет <see cref="ChapterId"/>, у страницы вне главы
/// нет ни <see cref="ChapterId"/>, ни <see cref="Chapter"/>. Поэтому всё nullable: <c>null</c> тут
/// значит «для этого вида такого поля нет», а не «пусто».
/// </para>
/// <para>
/// Это ОТДЕЛЬНАЯ модель, а не книга или страница целиком: поиск отдаёт свой урезанный набор полей
/// (нет ни описания, ни тела, ни владельца), и подсовывать его под видом полной сущности значило бы
/// обещать поля, которых в ответе не было. Прочитать сущность целиком можно по
/// <see cref="Id"/> обычным маршрутом чтения.
/// </para>
/// </remarks>
public sealed class BookStackSearchResult
{
    /// <summary>Идентификатор найденной сущности в пределах её вида.</summary>
    /// <remarks>
    /// Уникален только вместе с <see cref="Type"/>: книга 10 и страница 10 это разные объекты.
    /// </remarks>
    public int? Id { get; set; }

    /// <summary>
    /// Вид сущности: <c>bookshelf</c>, <c>book</c>, <c>chapter</c> или <c>page</c>. Значения
    /// собраны в <see cref="BookStackEntityType"/>.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>Имя.</summary>
    public string? Name { get; set; }

    /// <summary>Короткое имя в адресах.</summary>
    public string? Slug { get; set; }

    /// <summary>Готовый адрес в интерфейсе. Собран сервером, склеивать его самим не нужно.</summary>
    public string? Url { get; set; }

    /// <summary>Книга, которой принадлежит глава или страница. У книг и полок поля нет.</summary>
    public int? BookId { get; set; }

    /// <summary>Глава, которой принадлежит страница. Нет у страниц вне главы (замерено).</summary>
    public int? ChapterId { get; set; }

    /// <summary>Признак черновика. Есть только у страниц.</summary>
    public bool? Draft { get; set; }

    /// <summary>Признак шаблона. Есть только у страниц.</summary>
    public bool? Template { get; set; }

    /// <summary>Когда создано.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда изменено.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Книга-родитель кратко. Приходит у глав и страниц.</summary>
    public BookStackSearchParentRef? Book { get; set; }

    /// <summary>Глава-родитель кратко. Приходит только у страниц внутри главы.</summary>
    public BookStackSearchParentRef? Chapter { get; set; }

    /// <summary>Метки сущности.</summary>
    public List<BookStackSearchTag>? Tags { get; set; }

    /// <summary>Куски текста с подсветкой совпадений.</summary>
    public BookStackSearchPreview? PreviewHtml { get; set; }
}

/// <summary>Родитель найденной сущности: идентификатор, имя и короткое имя.</summary>
public sealed class BookStackSearchParentRef
{
    /// <summary>Идентификатор.</summary>
    public int? Id { get; set; }

    /// <summary>Имя.</summary>
    public string? Name { get; set; }

    /// <summary>Короткое имя.</summary>
    public string? Slug { get; set; }
}

/// <summary>Метка найденной сущности.</summary>
/// <remarks>
/// Тип свой, а не общий с содержимым: поиск отдаёт метку тремя полями и не обещает ничего сверх
/// того. Именно по этим именам и значениям ищет фильтр меток, см.
/// <see cref="BookStackSdk.Search.BookStackSearchQuery.AddTag"/>.
/// </remarks>
public sealed class BookStackSearchTag
{
    /// <summary>Имя метки.</summary>
    public string? Name { get; set; }

    /// <summary>Значение метки. Пустое у меток без значения.</summary>
    public string? Value { get; set; }

    /// <summary>Порядок показа.</summary>
    public int? Order { get; set; }
}

/// <summary>
/// Подсветка совпадений.
/// </summary>
/// <remarks>
/// Оба поля приходят с разметкой HTML: совпавшие слова обёрнуты в <c>&lt;strong&gt;</c>. Это готовый
/// кусок разметки, а не текст, и выводить его как есть в HTML-страницу небезопасно, если содержимое
/// заводят не только доверенные редакторы. Чистить его здесь мы не будем: решение о том, что
/// вырезать, принимает тот, кто выводит.
/// <para>
/// Когда в запросе нет ни одного текстового термина (например, только <c>{updated_after:...}</c>),
/// подсветки нет, а поля всё равно приходят: в них просто имя и начало текста (замерено).
/// </para>
/// </remarks>
public sealed class BookStackSearchPreview
{
    /// <summary>Имя сущности с подсветкой.</summary>
    public string? Name { get; set; }

    /// <summary>Куски текста с подсветкой, склеенные многоточиями.</summary>
    public string? Content { get; set; }
}

/// <summary>
/// Значения поля <see cref="BookStackSearchResult.Type"/> и фильтра <c>{type:...}</c>.
/// </summary>
/// <remarks>
/// Именно эти четыре строки понимает поиск. Обратите внимание на полку: в выдаче и в фильтре она
/// зовётся <c>bookshelf</c>, хотя её маршруты называются <c>/api/shelves</c>. Замерено:
/// <c>{type:page|book}</c> сузил выдачу до страниц и книг.
/// </remarks>
public static class BookStackEntityType
{
    /// <summary>Полка.</summary>
    public const string Bookshelf = "bookshelf";

    /// <summary>Книга.</summary>
    public const string Book = "book";

    /// <summary>Глава.</summary>
    public const string Chapter = "chapter";

    /// <summary>Страница.</summary>
    public const string Page = "page";
}
