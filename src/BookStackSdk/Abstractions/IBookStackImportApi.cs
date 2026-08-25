using BookStackSdk.Internal;
using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>Куда кладётся импортируемая страница или глава.</summary>
/// <remarks>
/// Значения совпадают со строками, которых ждёт поле <c>parent_type</c>. Перечисление заведено
/// вместо строки потому, что ошибиться тут можно ровно двумя способами — опечаткой и словом
/// <c>shelf</c>, — и оба сервер отвергает уже после того, как архив загружен, то есть с висящим
/// импортом на руках.
/// </remarks>
public enum BookStackImportParent
{
    /// <summary>Книга. Годится и главе, и странице.</summary>
    Book = 0,

    /// <summary>Глава. Годится только странице.</summary>
    Chapter,
}

/// <summary>
/// Импорт содержимого из архива ZIP: перенос книг, глав и страниц между установками BookStack.
/// </summary>
/// <remarks>
/// Это вторая половина пары к <see cref="IBookStackExportApi"/>: архив, полученный там
/// <see cref="BookStackExportFormat.Zip"/>, принимается здесь. Ничего иного эти маршруты не берут,
/// произвольный ZIP отвергается проверкой.
/// <para>
/// ВАЖНО, и это главное, что надо знать до первого вызова: ИМПОРТ ВСЕГДА СОЗДАЁТ НОВОЕ. Обновления
/// существующей книги по совпадению имени или слага тут нет и не предполагается: повторный импорт
/// того же архива даёт вторую книгу рядом с первой, а не заменяет её. Кому нужна замена, тот
/// удаляет прежнюю сам, и помнит про мягкое удаление (см. <see cref="IBookStackRecycleBinApi"/>):
/// короткое имя удалённая книга не держит, но сама остаётся в корзине, и восстановление её после
/// импорта даёт две книги с одним слагом.
/// </para>
/// <para>
/// ПОЛКИ через импорт не переносятся. Их не выгружает <see cref="IBookStackExportApi"/> (маршрута
/// нет), значит и импортировать нечего: полка на приёмнике собирается заново через
/// <see cref="IBookStackContentApi.CreateShelfAsync"/> с перечислением новых книг.
/// </para>
/// <para>
/// Права: все пять маршрутов требуют права на импорт содержимого, отдельного от доступа к API.
/// </para>
/// </remarks>
public interface IBookStackImportApi
{
    /// <summary>Окно списка висящих импортов (<c>GET /api/imports</c>).</summary>
    /// <remarks>
    /// В списке видны только импорты, доступные текущему токену. Разбора содержимого архива
    /// (<see cref="BookStackImport.Details"/>) тут нет, за ним надо идти чтением одиночного.
    /// </remarks>
    Task<BookStackPage<BookStackImport>> ListAsync(
        int? count = null, int? offset = null, string? sort = null, CancellationToken ct = default);

    /// <summary>Импорт целиком (<c>GET /api/imports/{id}</c>): с разбором содержимого архива.</summary>
    Task<BookStackImport?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Загружает архив (<c>POST /api/imports</c>). Содержимое при этом НЕ создаётся.
    /// </summary>
    /// <param name="fileName">Имя файла в теле запроса. Служебное: название берётся из архива.</param>
    /// <param name="zip">Архив, полученный выгрузкой в <see cref="BookStackExportFormat.Zip"/>.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// Тело многочастное, поле называется <c>file</c>. Ограничение размера на стороне сервера
    /// объявлено в живой доке как <c>max:50000</c>, то есть около 50 МБ, и упирается ещё и в
    /// настройки PHP (<c>upload_max_filesize</c>, <c>post_max_size</c>): книга с тяжёлыми
    /// картинками может не пройти, и ответом на это будет отказ веб-сервера, а не BookStack.
    /// <para>
    /// Возвращённый импорт надо ЗАПУСТИТЬ, иначе он так и останется висеть, см.
    /// <see cref="BookStackImport"/>.
    /// </para>
    /// </remarks>
    Task<BookStackImport?> UploadAsync(string fileName, byte[] zip, CancellationToken ct = default);

    /// <summary>
    /// Запускает импорт книги (<c>POST /api/imports/{id}</c>) и отдаёт созданную книгу.
    /// </summary>
    /// <remarks>
    /// Родитель книге не нужен и не передаётся. Вызов у книг с картинками и вложениями идёт долго:
    /// разбор архива и запись файлов происходят внутри этого запроса, а не фоном, так что
    /// <see cref="BookStackOptions.Timeout"/> тут значит больше, чем где-либо ещё.
    /// </remarks>
    Task<BookStackBook?> RunAsBookAsync(int importId, CancellationToken ct = default);

    /// <summary>
    /// Запускает импорт главы в указанную книгу и отдаёт созданную главу.
    /// </summary>
    /// <param name="importId">Импорт с видом <see cref="BookStackImportType.Chapter"/>.</param>
    /// <param name="bookId">Книга-родитель. Обязательна: без неё главе негде лежать.</param>
    /// <param name="ct">Отмена.</param>
    Task<BookStackChapter?> RunAsChapterAsync(int importId, int bookId, CancellationToken ct = default);

    /// <summary>
    /// Запускает импорт страницы в указанную книгу или главу и отдаёт созданную страницу.
    /// </summary>
    /// <param name="importId">Импорт с видом <see cref="BookStackImportType.Page"/>.</param>
    /// <param name="parentType">Куда класть: книга или глава.</param>
    /// <param name="parentId">Идентификатор родителя.</param>
    /// <param name="ct">Отмена.</param>
    Task<BookStackPage?> RunAsPageAsync(
        int importId, BookStackImportParent parentType, int parentId, CancellationToken ct = default);

    /// <summary>
    /// Удаляет висящий импорт (<c>DELETE /api/imports/{id}</c>) вместе с архивом.
    /// </summary>
    /// <remarks>
    /// Удаление тут, в отличие от содержимого, НЕ мягкое: корзина хранит книги и страницы, а не
    /// загруженные архивы. Отменять нечего — импорт ещё ничего не создал.
    /// </remarks>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
