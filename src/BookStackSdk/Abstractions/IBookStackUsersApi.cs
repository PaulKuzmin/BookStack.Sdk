using BookStackSdk.Internal;
using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Пользователи (<c>/api/users</c>).
/// </summary>
/// <remarks>
/// Все маршруты требуют права управления пользователями: без него приходит 403, а не пустой
/// список. Роли тут только назначаются идентификаторами; читать и заводить их надо через
/// <see cref="IBookStackRolesApi"/>, потому что список пользователей ролей не отдаёт вовсе.
/// </remarks>
public interface IBookStackUsersApi
{
    /// <summary>
    /// Список пользователей (<c>GET /api/users</c>).
    /// </summary>
    /// <remarks>
    /// В списке есть <c>last_activity_at</c>, но НЕТ ролей. Фильтры и их молчаливое игнорирование
    /// описаны в <see cref="BookStackUserListQuery"/>.
    /// </remarks>
    Task<BookStackPage<BookStackUser>> ListAsync(
        BookStackUserListQuery? query = null, CancellationToken ct = default);

    /// <summary>
    /// Чтение пользователя (<c>GET /api/users/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Тут есть роли, но НЕТ <c>last_activity_at</c>: замерено на пользователе 1, у которого
    /// активность заведомо была. То есть свести полную картину по одному пользователю можно только
    /// двумя вызовами, списком с фильтром и чтением.
    /// </remarks>
    Task<BookStackUser?> GetAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Создаёт пользователя (<c>POST /api/users</c>).
    /// </summary>
    /// <remarks>
    /// В ответе приходят роли. Переданный пароль сохраняется как есть, молчаливой подмены,
    /// как в MantisBT, тут нет (проверено вживую).
    /// </remarks>
    Task<BookStackUser?> CreateAsync(BookStackCreateUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Изменяет пользователя (<c>PUT /api/users/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Неупомянутые поля не трогаются, но переданный список ролей заменяет прежний целиком.
    /// Правка имени меняет короткое имя (замерено), а на него смотрят фильтры поиска по
    /// пользователю.
    /// </remarks>
    Task<BookStackUser?> UpdateAsync(int id, BookStackUpdateUserRequest request, CancellationToken ct = default);

    /// <summary>
    /// Удаляет пользователя (<c>DELETE /api/users/{id}</c>), при желании передавая владение его
    /// содержимым другому.
    /// </summary>
    /// <param name="id">Кого удалить.</param>
    /// <param name="request">
    /// Кому передать владение. <c>null</c> означает не передавать никому: содержимое останется
    /// без владельца.
    /// </param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// Удаление пользователя НЕ мягкое: в корзину он не уезжает, второго шанса нет. Получатель
    /// владения не проверяется вопреки заявленному в живой доке правилу, подробности и замер
    /// в <see cref="BookStackDeleteUserRequest.MigrateOwnershipId"/>.
    /// <para>
    /// Тело у <c>DELETE</c> непривычно, но именно так устроен маршрут: контроллер читает поле
    /// из тела запроса. Проверено вживую.
    /// </para>
    /// </remarks>
    Task DeleteAsync(int id, BookStackDeleteUserRequest? request = null, CancellationToken ct = default);
}
