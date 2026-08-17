using BookStackSdk.Internal;
using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Роли (<c>/api/roles</c>).
/// </summary>
/// <remarks>
/// Отдельный интерфейс, а не часть пользователей, по прикладной причине: список пользователей
/// ролей НЕ отдаёт (замерено), и восстанавливать картину «кто что может» приходится отсюда.
/// Дешёвый путь для этого один: прочитать роль целиком и взять её состав
/// (<see cref="BookStackRole.Users"/>), а не перебирать пользователей поштучно.
/// <para>
/// Права требуются свои: управление ролями, а не управление пользователями. Одно без другого
/// вполне бывает.
/// </para>
/// </remarks>
public interface IBookStackRolesApi
{
    /// <summary>
    /// Список ролей (<c>GET /api/roles</c>).
    /// </summary>
    /// <param name="count">Сколько вернуть.</param>
    /// <param name="offset">Сколько пропустить.</param>
    /// <param name="sort">Поле сортировки.</param>
    /// <param name="displayName">Точное совпадение отображаемого имени (<c>filter[display_name]</c>).</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// В списке приходят счётчики (<c>users_count</c>, <c>permissions_count</c>), но НЕ приходят
    /// сами права и состав.
    /// <para>
    /// ВАЖНО: фильтровать можно только по полям списка, а <c>id</c> и <c>system_name</c> в него
    /// не входят (исходник <c>RoleApiController::list</c>). Замерено: <c>?filter[id]=1</c> вернул
    /// ВСЕ роли, без единого признака того, что условие выброшено. Читать роль по идентификатору
    /// надо чтением, а не фильтром списка.
    /// </para>
    /// </remarks>
    Task<BookStackPage<BookStackRole>> ListAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        string? displayName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Чтение роли (<c>GET /api/roles/{id}</c>): права строками и состав пользователей.
    /// </summary>
    /// <remarks>Счётчиков тут нет, они есть только в списке.</remarks>
    Task<BookStackRole?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Создаёт роль (<c>POST /api/roles</c>).
    /// </summary>
    /// <remarks>
    /// Неизвестное имя права молча выбрасывается, ответ при этом 200. Проверять состав надо
    /// по ответу, замер в <see cref="BookStackRole.Permissions"/>.
    /// </remarks>
    Task<BookStackRole?> CreateAsync(BookStackCreateRoleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Изменяет роль (<c>PUT /api/roles/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Переданный список прав заменяет прежний ЦЕЛИКОМ, пустой список снимает все права (замерено).
    /// Чтобы добавить одно право, сначала прочитайте роль.
    /// </remarks>
    Task<BookStackRole?> UpdateAsync(int id, BookStackUpdateRoleRequest request, CancellationToken ct = default);

    /// <summary>
    /// Удаляет роль (<c>DELETE /api/roles/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Встроенные роли (с непустым <see cref="BookStackRole.SystemName"/>) удалению не подлежат:
    /// сервер откажет. Пользователи удалённой роли её просто теряют.
    /// </remarks>
    Task DeleteAsync(int id, CancellationToken ct = default);
}
