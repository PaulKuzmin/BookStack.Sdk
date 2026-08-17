using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Содержимое BookStack: полки, книги, главы и страницы.
/// </summary>
/// <remarks>
/// Четыре сущности собраны в один интерфейс потому, что у них общий транспорт (один и тот же
/// <c>ListingResponseBuilder</c> на все списки), общая форма ответа и общие ловушки. Разводить их
/// по четырём интерфейсам значило бы четырежды переписать одни и те же примечания про молчаливое
/// усечение фильтров и про мягкое удаление.
/// <para>
/// Чего в этом интерфейсе НЕТ и почему:
/// </para>
/// <list type="bullet">
/// <item>обложек книг и полок: они уходят многочастным телом, это отдельный путь
/// (<see cref="Internal.BookStackMultipart"/>);</item>
/// <item>выгрузок в PDF, HTML, markdown, простой текст и ZIP: это двоичное чтение, отдельная
/// часть SDK;</item>
/// <item>поиска: у BookStack он один на всё содержимое, <c>GET /api/search</c>, со своим языком
/// запроса;</item>
/// <item>корзины: восстановление и окончательное удаление живут на своих маршрутах
/// <c>/api/recycle-bin</c>, см. примечания к методам удаления;</item>
/// <item>перестановки глав и страниц внутри книги одним запросом: такого маршрута у BookStack нет
/// вовсе (проверено по <c>/api/docs.json</c> и по списку маршрутов), порядок задаётся полем
/// <c>priority</c> у каждой сущности по отдельности.</item>
/// </list>
/// </remarks>
public interface IBookStackContentApi
{
    // ---- Страницы ----

    /// <summary>Окно списка страниц (<c>GET /api/pages</c>).</summary>
    /// <remarks>
    /// В списке НЕТ ни содержимого страницы, ни тегов: они приходят только при чтении одиночной
    /// страницы. Что можно ставить в <see cref="BookStackListQuery.SortBy"/> и в фильтры, см.
    /// <see cref="BookStackSortFields.Pages"/>, и обязательно прочитать про молчаливое усечение в
    /// описании <see cref="BookStackListQuery"/>.
    /// </remarks>
    Task<BookStackListResult<BookStackPage>> ListPagesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>
    /// Инкрементальный обход всех страниц, подходящих под запрос: сам двигает окно.
    /// </summary>
    /// <remarks>
    /// Для выборки «что изменилось с прошлого раза» задавать
    /// <see cref="BookStackListQuery.UpdatedAfter"/> и сортировку по <c>updated_at</c> по
    /// возрастанию. Оговорки про устойчивость обхода в описании реализации.
    /// </remarks>
    IAsyncEnumerable<BookStackPage> EnumeratePagesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>
    /// Страница целиком (<c>GET /api/pages/{id}</c>): с <c>html</c>, <c>markdown</c>,
    /// <c>raw_html</c> и тегами.
    /// </summary>
    Task<BookStackPage?> GetPageAsync(int id, CancellationToken ct = default);

    /// <summary>Создать страницу из markdown (<c>POST /api/pages</c>).</summary>
    Task<BookStackPage?> CreatePageAsync(BookStackPageCreate page, CancellationToken ct = default);

    /// <summary>
    /// Обновить страницу (<c>PUT /api/pages/{id}</c>). Обновление частичное: неупомянутые поля
    /// остаются как были.
    /// </summary>
    Task<BookStackPage?> UpdatePageAsync(int id, BookStackPageUpdate page, CancellationToken ct = default);

    /// <summary>
    /// Удалить страницу (<c>DELETE /api/pages/{id}</c>).
    /// </summary>
    /// <remarks>
    /// УДАЛЕНИЕ МЯГКОЕ. Страница уезжает в корзину (<c>/api/recycle-bin</c>) и продолжает занимать
    /// место, а её содержимое остаётся доступным тому, у кого есть права на корзину. Замерено на
    /// стенде 17.08.2026: ответ 204 без тела, последующий <c>GET</c> той же страницы даёт 404
    /// «Page not found», и при этом запись находится в корзине с полями <c>deletable_type: page</c>
    /// и <c>deletable_id</c> прежнего идентификатора.
    /// <para>
    /// Отсюда два следствия. Первое: восстановление возможно, но не отсюда, а через
    /// <c>PUT /api/recycle-bin/{deletionId}</c>, и <c>deletionId</c> это идентификатор ЗАПИСИ
    /// КОРЗИНЫ, а не удалённой страницы. Второе: если удаление делалось ради того, чтобы данные
    /// исчезли, одного этого вызова НЕДОСТАТОЧНО, добивать надо отдельно через
    /// <c>DELETE /api/recycle-bin/{deletionId}</c>.
    /// </para>
    /// </remarks>
    Task DeletePageAsync(int id, CancellationToken ct = default);

    // ---- Книги ----

    /// <summary>Окно списка книг (<c>GET /api/books</c>).</summary>
    /// <remarks>
    /// В списке нет тегов, оглавления и <c>description_html</c>, а обложка приходит укороченной,
    /// тремя полями. Поля сортировки и фильтрации см. <see cref="BookStackSortFields.Books"/>.
    /// </remarks>
    Task<BookStackListResult<BookStackBook>> ListBooksAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>Инкрементальный обход всех книг, подходящих под запрос.</summary>
    IAsyncEnumerable<BookStackBook> EnumerateBooksAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>
    /// Книга целиком (<c>GET /api/books/{id}</c>): с тегами, обложкой, оглавлением и списком полок.
    /// </summary>
    /// <remarks>
    /// Оглавление (<see cref="BookStackBook.Contents"/>) приходит ТОЛЬКО отсюда: ни список книг, ни
    /// ответы создания и обновления его не содержат.
    /// </remarks>
    Task<BookStackBook?> GetBookAsync(int id, CancellationToken ct = default);

    /// <summary>Создать книгу (<c>POST /api/books</c>).</summary>
    Task<BookStackBook?> CreateBookAsync(BookStackBookCreate book, CancellationToken ct = default);

    /// <summary>Обновить книгу (<c>PUT /api/books/{id}</c>). Обновление частичное.</summary>
    Task<BookStackBook?> UpdateBookAsync(int id, BookStackBookCreate book, CancellationToken ct = default);

    /// <summary>
    /// Удалить книгу (<c>DELETE /api/books/{id}</c>).
    /// </summary>
    /// <remarks>
    /// УДАЛЕНИЕ МЯГКОЕ, книга уезжает в корзину (<c>/api/recycle-bin</c>) вместе со всеми своими
    /// главами и страницами: в корзине она числится одной записью, и её счётчики
    /// <c>pages_count</c> и <c>chapters_count</c> показывают, сколько содержимого уехало с ней
    /// (замерено). Восстановление и окончательное удаление идут отдельными маршрутами корзины,
    /// подробности в <see cref="DeletePageAsync"/>.
    /// </remarks>
    Task DeleteBookAsync(int id, CancellationToken ct = default);

    // ---- Главы ----

    /// <summary>Окно списка глав (<c>GET /api/chapters</c>).</summary>
    /// <remarks>
    /// В списке нет ни тегов, ни списка страниц. Поля сортировки и фильтрации см.
    /// <see cref="BookStackSortFields.Chapters"/>. Отобрать главы одной книги можно фильтром
    /// <c>book_id</c>: замер на стенде 17.08.2026 дал <c>total=1</c> на свою книгу и <c>total=0</c>
    /// на чужую, тогда как фильтр по несуществующему полю вернул ВСЕ три главы стенда, то есть
    /// был выброшен молча.
    /// </remarks>
    Task<BookStackListResult<BookStackChapter>> ListChaptersAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>Инкрементальный обход всех глав, подходящих под запрос.</summary>
    IAsyncEnumerable<BookStackChapter> EnumerateChaptersAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>Глава целиком (<c>GET /api/chapters/{id}</c>): с тегами и списком своих страниц.</summary>
    Task<BookStackChapter?> GetChapterAsync(int id, CancellationToken ct = default);

    /// <summary>Создать главу (<c>POST /api/chapters</c>).</summary>
    Task<BookStackChapter?> CreateChapterAsync(BookStackChapterCreate chapter, CancellationToken ct = default);

    /// <summary>Обновить главу (<c>PUT /api/chapters/{id}</c>). Обновление частичное.</summary>
    Task<BookStackChapter?> UpdateChapterAsync(
        int id, BookStackChapterCreate chapter, CancellationToken ct = default);

    /// <summary>
    /// Удалить главу (<c>DELETE /api/chapters/{id}</c>).
    /// </summary>
    /// <remarks>
    /// УДАЛЕНИЕ МЯГКОЕ, глава уезжает в корзину (<c>/api/recycle-bin</c>) вместе со своими
    /// страницами. Подробности про корзину в <see cref="DeletePageAsync"/>.
    /// </remarks>
    Task DeleteChapterAsync(int id, CancellationToken ct = default);

    // ---- Полки ----

    /// <summary>Окно списка полок (<c>GET /api/shelves</c>).</summary>
    /// <remarks>
    /// В списке нет ни тегов, ни состава полки. Поля сортировки и фильтрации см.
    /// <see cref="BookStackSortFields.Shelves"/>.
    /// </remarks>
    Task<BookStackListResult<BookStackShelf>> ListShelvesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>Инкрементальный обход всех полок, подходящих под запрос.</summary>
    IAsyncEnumerable<BookStackShelf> EnumerateShelvesAsync(
        BookStackListQuery? query = null, CancellationToken ct = default);

    /// <summary>Полка целиком (<c>GET /api/shelves/{id}</c>): с тегами, обложкой и составом.</summary>
    Task<BookStackShelf?> GetShelfAsync(int id, CancellationToken ct = default);

    /// <summary>Создать полку (<c>POST /api/shelves</c>).</summary>
    Task<BookStackShelf?> CreateShelfAsync(BookStackShelfCreate shelf, CancellationToken ct = default);

    /// <summary>
    /// Обновить полку (<c>PUT /api/shelves/{id}</c>). Обновление частичное, но состав полки
    /// замещается целиком, см. <see cref="BookStackShelfCreate.Books"/>.
    /// </summary>
    Task<BookStackShelf?> UpdateShelfAsync(int id, BookStackShelfCreate shelf, CancellationToken ct = default);

    /// <summary>
    /// Удалить полку (<c>DELETE /api/shelves/{id}</c>).
    /// </summary>
    /// <remarks>
    /// УДАЛЕНИЕ МЯГКОЕ, полка уезжает в корзину (<c>/api/recycle-bin</c>). Книги при этом НЕ
    /// удаляются и в корзину не едут: полка это группировка, а не владелец, книга без полки живёт
    /// как обычно. Подробности про корзину в <see cref="DeletePageAsync"/>.
    /// </remarks>
    Task DeleteShelfAsync(int id, CancellationToken ct = default);
}
