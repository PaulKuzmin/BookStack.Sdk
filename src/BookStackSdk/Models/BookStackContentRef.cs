namespace BookStackSdk.Models;

/// <summary>
/// Элемент оглавления книги: страница или глава, лежащая в книге напрямую
/// (поле <c>contents</c> ответа <c>GET /api/books/{id}</c>).
/// </summary>
/// <remarks>
/// Тип разнородный намеренно: оглавление это ровно то дерево, которое BookStack показывает при
/// просмотре книги, и в нём вперемешку идут главы и страницы. Что именно перед нами, говорит поле
/// <see cref="Type"/>: <c>chapter</c> или <c>page</c>. У главы заполнен <see cref="Pages"/>,
/// у страницы он <c>null</c>.
/// <para>
/// ВАЖНО: сборщик оглавления ВЫБРАСЫВАЕТ поля со значением <c>null</c>, а не отдаёт их пустыми.
/// В исходнике <c>app/Api/ApiEntityListFormatter.php</c>, метод <c>formatSingle()</c>, стоит
/// <c>if (!isset($values[$field])) continue;</c>. Замер на стенде 17.08.2026: у страницы, лежащей
/// в книге напрямую, поля <c>chapter_id</c> в оглавлении НЕТ вовсе, хотя в примере живой доки оно
/// показано как <c>null</c>. То есть отсутствие поля тут ничего не означает сверх «значение
/// пустое», и проверять надо на <c>null</c>, а не на наличие ключа.
/// </para>
/// <para>
/// Набор полей задан тем же сборщиком и постоянен: <c>id</c>, <c>name</c>, <c>slug</c>,
/// <c>book_id</c>, <c>chapter_id</c>, <c>draft</c>, <c>template</c>, <c>priority</c>,
/// <c>created_at</c>, <c>updated_at</c>, плюс вычисляемые <c>url</c> и <c>type</c>. Ни тегов,
/// ни содержимого страницы, ни ссылок на пользователей тут нет: за ними надо идти отдельным
/// чтением сущности.
/// </para>
/// </remarks>
public sealed class BookStackContentRef
{
    /// <summary>Идентификатор страницы или главы.</summary>
    public int? Id { get; set; }

    /// <summary>Что это: <c>page</c> или <c>chapter</c>.</summary>
    public string? Type { get; set; }

    /// <summary>Заголовок.</summary>
    public string? Name { get; set; }

    /// <summary>Слаг. Пересчитывается при переименовании, см. <see cref="BookStackPage.Slug"/>.</summary>
    public string? Slug { get; set; }

    /// <summary>Книга, которой принадлежит элемент.</summary>
    public int? BookId { get; set; }

    /// <summary>
    /// Глава, если страница лежит в главе. Для элементов оглавления верхнего уровня поля в ответе
    /// нет вовсе (замерено), сюда попадает <c>null</c>.
    /// </summary>
    public int? ChapterId { get; set; }

    /// <summary>Черновик ли. Заполнено только у страниц.</summary>
    public bool? Draft { get; set; }

    /// <summary>Шаблон ли. Заполнено только у страниц.</summary>
    public bool? Template { get; set; }

    /// <summary>Позиция в книге. Ею задаётся порядок показа.</summary>
    public int? Priority { get; set; }

    /// <summary>Когда создано. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда изменено. UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Полный адрес просмотра. Считается сервером от базового адреса установки, поэтому при
    /// несовпадении <c>APP_URL</c> с реальным адресом открытия он будет указывать не туда.
    /// </summary>
    public string? Url { get; set; }

    /// <summary>
    /// Страницы главы. Заполнено только у элементов с <see cref="Type"/> равным <c>chapter</c>,
    /// у страниц <c>null</c> (сборщик выбрасывает поле, вернувшее <c>null</c>).
    /// </summary>
    public List<BookStackContentRef>? Pages { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Type} #{Id} {Name}";
}
