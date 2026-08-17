namespace BookStackSdk.Models;

/// <summary>
/// Роль BookStack: набор прав плюс список тех, кому она выдана.
/// </summary>
/// <remarks>
/// Роли живут отдельным интерфейсом (<see cref="BookStackSdk.Abstractions.IBookStackRolesApi"/>)
/// не для симметрии с маршрутами, а потому что список пользователей ролей НЕ отдаёт: их приходится
/// дотягивать отдельно, поштучным чтением пользователя или чтением роли целиком.
/// <para>
/// Наборы полей у списка и чтения тут тоже разные, и опять не вложенные один в другой (замерено
/// 17.08.2026 на роли 2):
/// <list type="bullet">
/// <item><c>GET /api/roles</c> отдаёт <c>users_count</c> и <c>permissions_count</c>, но НЕ отдаёт
/// сами <c>permissions</c> и <c>users</c>;</item>
/// <item><c>GET /api/roles/{id}</c> отдаёт <c>permissions</c> (списком строк) и <c>users</c>, но
/// НЕ отдаёт счётчиков.</item>
/// </list>
/// Поэтому и счётчики, и списки nullable: <c>null</c> это «в этом ответе поля не было», пустой
/// список это «прав нет» (такое бывает, см. примечание к <see cref="Permissions"/>).
/// </para>
/// </remarks>
public sealed class BookStackRole
{
    /// <summary>Идентификатор.</summary>
    public int? Id { get; set; }

    /// <summary>Отображаемое имя, например <c>Editor</c>.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Описание.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Системное имя. Непустое только у встроенных ролей (<c>admin</c>, <c>public</c>), у прочих
    /// приходит пустой строкой (замерено). По нему отличают роли, которые нельзя удалять.
    /// </summary>
    public string? SystemName { get; set; }

    /// <summary>Внешний идентификатор роли для сопоставления с группой у провайдера SSO.</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>Требовать второй фактор всем в этой роли.</summary>
    public bool? MfaEnforced { get; set; }

    /// <summary>Сколько пользователей в роли. Приходит только в списке.</summary>
    public int? UsersCount { get; set; }

    /// <summary>Сколько прав у роли. Приходит только в списке.</summary>
    public int? PermissionsCount { get; set; }

    /// <summary>
    /// Права строками, например <c>book-view-all</c>. Приходят только при чтении, создании
    /// и изменении.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: неизвестное имя права принимается молча и НЕ выдаётся. Замерено 17.08.2026: создание
    /// роли с правом <c>выдуманное-право</c> вернуло 200 и <c>"permissions": []</c>, то есть роль
    /// создана вообще без прав, и по коду ответа этого не видно. Проверять состав надо ответом,
    /// а не фактом успеха, ровно как с молча выброшенными фильтрами списков.
    /// </remarks>
    public List<string>? Permissions { get; set; }

    /// <summary>Кому выдана. Приходит только при чтении, создании и изменении.</summary>
    public List<BookStackUserRef>? Users { get; set; }

    /// <summary>Когда создана.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда изменена.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Ссылка на роль внутри пользователя: <c>{"id": 1, "display_name": "Admin"}</c>.
/// </summary>
/// <remarks>
/// Отдельный тип, а не <see cref="BookStackRole"/>, потому что в этом месте приходят ровно два
/// поля. Класть их в полную модель значило бы отдавать наружу объект, у которого всё остальное
/// пусто, и путать «поля не было в ответе» с «поле пустое».
/// </remarks>
public sealed class BookStackRoleRef
{
    /// <summary>Идентификатор роли.</summary>
    public int? Id { get; set; }

    /// <summary>Отображаемое имя роли.</summary>
    public string? DisplayName { get; set; }
}

/// <summary>Тело создания роли (<c>POST /api/roles</c>).</summary>
public sealed class BookStackCreateRoleRequest
{
    /// <summary>Отображаемое имя. Обязательно, не короче трёх символов.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Описание.</summary>
    public string? Description { get; set; }

    /// <summary>Требовать второй фактор.</summary>
    public bool? MfaEnforced { get; set; }

    /// <summary>Внешний идентификатор роли для сопоставления с группой SSO.</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>
    /// Права строками. Неизвестные имена молча выбрасываются, см.
    /// <see cref="BookStackRole.Permissions"/>.
    /// </summary>
    public List<string>? Permissions { get; set; }
}

/// <summary>Тело изменения роли (<c>PUT /api/roles/{id}</c>).</summary>
/// <remarks>
/// ВАЖНО: <see cref="Permissions"/> заменяет набор прав ЦЕЛИКОМ, а не дополняет его. Пустой список
/// снимает все права (замерено: <c>{"permissions": []}</c> вернуло роль с пустым набором). Чтобы
/// добавить одно право, надо сначала прочитать роль и отправить прежний набор плюс новое; иначе
/// правка описания снесёт права. Неупомянутые поля при этом не трогаются: <c>null</c> в тело
/// не уходит.
/// </remarks>
public sealed class BookStackUpdateRoleRequest
{
    /// <summary>Отображаемое имя.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Описание.</summary>
    public string? Description { get; set; }

    /// <summary>Требовать второй фактор.</summary>
    public bool? MfaEnforced { get; set; }

    /// <summary>Внешний идентификатор роли.</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>Права строками. Заменяют прежний набор целиком.</summary>
    public List<string>? Permissions { get; set; }
}
