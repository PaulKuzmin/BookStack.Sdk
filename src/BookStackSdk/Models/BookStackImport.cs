using System.Text.Json;

namespace BookStackSdk.Models;

/// <summary>
/// Отложенный импорт: архив ZIP уже лежит на сервере, но ещё не разобран
/// (<c>/api/imports</c>).
/// </summary>
/// <remarks>
/// ВАЖНО: импорт это ДВА вызова, а не один. Загрузка (<c>POST /api/imports</c>) только принимает и
/// проверяет архив, содержимое при этом не создаётся; создаёт его отдельный запуск
/// (<c>POST /api/imports/{id}</c>). Разделение не наше и обойти его нечем: маршрута «загрузить и
/// сразу применить» у BookStack нет.
/// <para>
/// Отсюда следствие для вызывающего: между двумя вызовами есть состояние, которое переживает сбой.
/// Упавший после загрузки процесс оставляет на сервере висящий импорт, и он останется висеть, пока
/// его не запустят или не удалят (<c>DELETE /api/imports/{id}</c>). Срока жизни живая дока не
/// называет, поэтому и SDK на него не рассчитывает.
/// </para>
/// <para>
/// Все пять маршрутов импорта требуют права на импорт содержимого («permission to import content»),
/// и это отдельное право, а не общий доступ к API: токен, которым читаются книги, вполне может не
/// уметь импортировать.
/// </para>
/// </remarks>
public sealed class BookStackImport
{
    /// <summary>Идентификатор импорта. Он же нужен для запуска и для удаления.</summary>
    public int? Id { get; set; }

    /// <summary>Название содержимого архива: имя книги, главы или страницы, лежащей внутри.</summary>
    /// <remarks>Это НЕ имя загруженного файла: имя берётся из данных архива.</remarks>
    public string? Name { get; set; }

    /// <summary>Путь хранения архива на сервере, например <c>uploads/files/imports/…zip</c>.</summary>
    public string? Path { get; set; }

    /// <summary>Размер архива в байтах.</summary>
    public long? Size { get; set; }

    /// <summary>
    /// Что внутри архива: <c>book</c>, <c>chapter</c> или <c>page</c>, см.
    /// <see cref="BookStackImportType"/>.
    /// </summary>
    /// <remarks>
    /// Вид определяет сервер по содержимому архива, а не мы при загрузке. От него зависит, нужен ли
    /// запуску родитель: книге не нужен, главе и странице обязателен.
    /// </remarks>
    public string? Type { get; set; }

    /// <summary>Кто загрузил.</summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Когда загружен. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда запись менялась. UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Разбор содержимого архива: что именно будет создано при запуске.
    /// </summary>
    /// <remarks>
    /// Приходит только при чтении одиночного импорта (<c>GET /api/imports/{id}</c>), в списке этого
    /// поля нет. Тип оставлен сырым намеренно: форма зависит от вида импорта (у книги внутри главы
    /// и страницы, у страницы ни того, ни другого), и заводить под неё модель значило бы обещать
    /// разбор, которого мы не проверяли. Кому нужен состав, разбирает <see cref="JsonElement"/> сам.
    /// </remarks>
    public JsonElement? Details { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"импорт #{Id} {Type} {Name}";
}

/// <summary>Виды импорта: что лежит в архиве.</summary>
public static class BookStackImportType
{
    /// <summary>Книга. Запуск родителя не требует.</summary>
    public const string Book = "book";

    /// <summary>Глава. Запуску нужна книга-родитель.</summary>
    public const string Chapter = "chapter";

    /// <summary>Страница. Запуску нужен родитель: книга либо глава.</summary>
    public const string Page = "page";
}
