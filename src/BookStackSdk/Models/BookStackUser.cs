using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStackSdk.Models;

/// <summary>
/// Учётная запись BookStack.
/// </summary>
/// <remarks>
/// ВАЖНО про два разных набора полей. Список и чтение отдают РАЗНЫЕ наборы, и ни один не является
/// подмножеством другого (замерено на стенде 17.08.2026):
/// <list type="bullet">
/// <item><c>GET /api/users</c> отдаёт <c>last_activity_at</c>, но НЕ отдаёт <c>roles</c>;</item>
/// <item><c>GET /api/users/{id}</c> отдаёт <c>roles</c>, но НЕ отдаёт <c>last_activity_at</c>
/// (проверено на пользователе 1, у которого активность заведомо есть).</item>
/// </list>
/// Отсюда все поля nullable, включая <see cref="Roles"/>: <c>null</c> означает «в этом ответе поля
/// не было», пустой список означает «ролей нет». Подставлять на месте первого второе нельзя, иначе
/// код, читающий роли из списка, решит, что пользователь без ролей, и снимет ему доступ.
/// Роли по списку пользователей дотягиваются поштучно через <see cref="BookStackSdk.Abstractions.IBookStackRolesApi"/>
/// или чтением каждого пользователя.
/// </remarks>
public sealed class BookStackUser
{
    /// <summary>Идентификатор.</summary>
    public int? Id { get; set; }

    /// <summary>Отображаемое имя.</summary>
    public string? Name { get; set; }

    /// <summary>Почта. У BookStack это и есть логин: своего отдельного логина нет.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Короткое имя в адресах.
    /// </summary>
    /// <remarks>
    /// Считается из <see cref="Name"/> и МЕНЯЕТСЯ при переименовании (замерено: правка имени на
    /// «Проба SDK, правка» сменила slug с <c>proba-sdk</c> на <c>proba-sdk-pravka</c>). Хранить его
    /// у себя как ключ нельзя. При этом именно slug, а не идентификатор и не почта, ждут фильтры
    /// поиска <c>created_by</c>, <c>updated_by</c> и <c>owned_by</c>, см.
    /// <see cref="BookStackSdk.Search.BookStackSearchQuery"/>.
    /// </remarks>
    public string? Slug { get; set; }

    /// <summary>
    /// Внешний идентификатор для SSO. Это то самое поле, куда BookStack кладёт <c>sub</c> из OIDC.
    /// </summary>
    /// <remarks>
    /// Пустая строка и <c>null</c> тут значат разное: пустую строку сервер отдаёт для учёток без
    /// привязки к внешнему провайдеру (замерено), а <c>null</c> будет означать, что поля в ответе
    /// не было. Поэтому оно не нормализуется.
    /// </remarks>
    public string? ExternalAuthId { get; set; }

    /// <summary>Когда создан.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда изменён.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Последняя активность. Приходит только в списке, при чтении одного пользователя поля нет.
    /// <c>null</c> у ни разу не входившего (замерено).
    /// </summary>
    public DateTimeOffset? LastActivityAt { get; set; }

    /// <summary>Адрес профиля в интерфейсе.</summary>
    public string? ProfileUrl { get; set; }

    /// <summary>Адрес страницы правки в интерфейсе.</summary>
    public string? EditUrl { get; set; }

    /// <summary>Адрес аватара. Для учёток без загруженного аватара это общая заглушка установки.</summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Роли. Приходят при чтении, создании и изменении, но НЕ в списке. См. примечание к классу.
    /// </summary>
    public List<BookStackRoleRef>? Roles { get; set; }
}

/// <summary>
/// Ссылка на пользователя в чужом ответе: у картинки и вложения это <c>created_by</c> и
/// <c>updated_by</c>, у роли это состав <c>users</c>.
/// </summary>
/// <remarks>
/// ВАЖНО: у этого поля ДВЕ формы на одном и том же имени, и это не опечатка в доке, а замер
/// (17.08.2026, одна и та же картинка):
/// <list type="bullet">
/// <item><c>GET /api/image-gallery</c> (список) отдаёт <c>"created_by": 1</c>, то есть число;</item>
/// <item><c>GET /api/image-gallery/{id}</c> (чтение) отдаёт
/// <c>"created_by": {"id":1,"name":"Admin","slug":"admin"}</c>, то есть объект.</item>
/// </list>
/// Ровно то же самое у вложений. Поэтому тип читается своим преобразователем
/// (<see cref="BookStackUserRefConverter"/>), а не двумя разными моделями: две модели заставили бы
/// вызывающего знать, каким маршрутом получены данные, и это знание рано или поздно потерялось бы.
/// Из числа заполняется только <see cref="Id"/>, остальное остаётся <c>null</c>, что честно: имени
/// в таком ответе не было.
/// </remarks>
[JsonConverter(typeof(BookStackUserRefConverter))]
public sealed class BookStackUserRef
{
    /// <summary>Идентификатор пользователя.</summary>
    public int? Id { get; set; }

    /// <summary>Отображаемое имя. <c>null</c>, если ответ содержал только число.</summary>
    public string? Name { get; set; }

    /// <summary>Короткое имя. <c>null</c>, если ответ содержал только число.</summary>
    public string? Slug { get; set; }
}

/// <summary>
/// Читает <see cref="BookStackUserRef"/> и из числа, и из объекта. Обоснование в примечании к
/// <see cref="BookStackUserRef"/>.
/// </summary>
internal sealed class BookStackUserRefConverter : JsonConverter<BookStackUserRef>
{
    public override BookStackUserRef? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.Number:
                return new BookStackUserRef { Id = reader.GetInt32() };

            case JsonTokenType.String:
                // Числа в строке допускает общая настройка сериализации, и раз она включена
                // на весь клиент, здесь тоже не отказываем: иначе поведение зависело бы от того,
                // разбирает поле преобразователь или нет.
                return int.TryParse(reader.GetString(), out var parsed)
                    ? new BookStackUserRef { Id = parsed }
                    : new BookStackUserRef();

            case JsonTokenType.StartObject:
                var result = new BookStackUserRef();
                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject)
                        return result;

                    if (reader.TokenType != JsonTokenType.PropertyName)
                        continue;

                    var property = reader.GetString();
                    reader.Read();

                    if (string.Equals(property, "id", StringComparison.OrdinalIgnoreCase))
                        result.Id = reader.TokenType == JsonTokenType.Number ? reader.GetInt32() : null;
                    else if (string.Equals(property, "name", StringComparison.OrdinalIgnoreCase))
                        result.Name = reader.GetString();
                    else if (string.Equals(property, "slug", StringComparison.OrdinalIgnoreCase))
                        result.Slug = reader.GetString();
                    else
                        reader.Skip();
                }

                return result;

            default:
                throw new JsonException(
                    $"Ссылка на пользователя пришла неожиданным типом {reader.TokenType}: ожидались число или объект.");
        }
    }

    public override void Write(Utf8JsonWriter writer, BookStackUserRef value, JsonSerializerOptions options)
    {
        // Обратно этот тип не отправляется ни одним маршрутом, но писать его исключением значило бы
        // ломать сериализацию всей модели при попытке залогировать её целиком.
        writer.WriteStartObject();
        if (value.Id is { } id)
            writer.WriteNumber("id", id);
        if (value.Name is not null)
            writer.WriteString("name", value.Name);
        if (value.Slug is not null)
            writer.WriteString("slug", value.Slug);
        writer.WriteEndObject();
    }
}

/// <summary>Тело создания пользователя (<c>POST /api/users</c>).</summary>
public sealed class BookStackCreateUserRequest
{
    /// <summary>Отображаемое имя. Обязательно.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Почта. Обязательна и уникальна.
    /// </summary>
    /// <remarks>
    /// Это единственный идентификатор входа в BookStack: логина как отдельного поля нет. Значение,
    /// не проходящее проверку почты, отвергается с 422, поэтому телефон в качестве общего
    /// идентификатора с MantisBT не годится (замерено на стенде: <c>79141234567</c> даёт 422).
    /// </remarks>
    public string? Email { get; set; }

    /// <summary>Внешний идентификатор SSO (значение <c>sub</c> от провайдера).</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>Код языка интерфейса, например <c>ru</c>.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// Пароль, не короче 8 символов.
    /// </summary>
    /// <remarks>
    /// В отличие от MantisBT, переданный пароль НЕ подменяется молча: замерено созданием учётки
    /// с известным паролем. Если пароль не задан и <see cref="SendInvite"/> не выставлен, учётка
    /// создаётся без пароля, и войти в неё можно только через SSO или после правки.
    /// </remarks>
    public string? Password { get; set; }

    /// <summary>
    /// Идентификаторы ролей. Пустой список означает «без ролей», а не «оставить как есть».
    /// </summary>
    public List<int>? Roles { get; set; }

    /// <summary>
    /// Отправить приглашение письмом.
    /// </summary>
    /// <remarks>
    /// Требует настроенной почты. На стенде SMTP нет, поэтому значение здесь не проверялось вживую,
    /// и его умолчание оставлено серверу (<c>null</c> не отправляется).
    /// </remarks>
    public bool? SendInvite { get; set; }
}

/// <summary>
/// Тело изменения пользователя (<c>PUT /api/users/{id}</c>).
/// </summary>
/// <remarks>
/// Неупомянутые поля не трогаются: <c>null</c> в теле не уходит (см. <c>BookStackJson</c>).
/// Отдельного тела от создания этот запрос отличается отсутствием <c>send_invite</c>: в правилах
/// маршрута изменения такого поля нет вовсе, и посылать его значило бы посылать мусор.
/// </remarks>
public sealed class BookStackUpdateUserRequest
{
    /// <summary>Отображаемое имя. Правка имени меняет <c>slug</c> (замерено).</summary>
    public string? Name { get; set; }

    /// <summary>Почта.</summary>
    public string? Email { get; set; }

    /// <summary>Внешний идентификатор SSO.</summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>Код языка интерфейса.</summary>
    public string? Language { get; set; }

    /// <summary>Новый пароль, не короче 8 символов.</summary>
    public string? Password { get; set; }

    /// <summary>
    /// Идентификаторы ролей. Список заменяет прежний набор целиком, а не дополняет его.
    /// </summary>
    public List<int>? Roles { get; set; }
}

/// <summary>Тело удаления пользователя (<c>DELETE /api/users/{id}</c>).</summary>
/// <remarks>
/// Тело у <c>DELETE</c> выглядит непривычно, но именно так это устроено у BookStack: контроллер
/// читает <c>$request-&gt;input('migrate_ownership_id')</c>, то есть значение берётся из тела
/// запроса. Проверено вживую: тело уходит и доезжает.
/// </remarks>
public sealed class BookStackDeleteUserRequest
{
    /// <summary>
    /// Кому передать владение содержимым удаляемого пользователя.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: значение НЕ проверяется, вопреки заявленному в живой доке правилу
    /// <c>exists:users,id</c>. Исходник <c>UserApiController::delete</c> не вызывает валидацию
    /// вообще, а <c>UserRepo::destroy</c> делает <c>User::query()-&gt;find($newOwnerId)</c> и при
    /// пустом результате передаёт <c>null</c>, то есть ОБНУЛЯЕТ владение вместо передачи. Замерено
    /// 17.08.2026: удаление с <c>migrate_ownership_id: 999999</c> вернуло 204, пользователь удалён,
    /// про несуществующего получателя не сказано ни слова. Проверять существование получателя
    /// должен вызывающий: второго шанса не будет, удаление пользователя не мягкое.
    /// </remarks>
    public int? MigrateOwnershipId { get; set; }
}

/// <summary>Параметры списка пользователей (<c>GET /api/users</c>).</summary>
/// <remarks>
/// ВАЖНО про молчаливое игнорирование. Фильтровать и сортировать можно ТОЛЬКО по полям, которые
/// маршрут объявил в своём списке: <c>id</c>, <c>name</c>, <c>slug</c>, <c>email</c>,
/// <c>external_auth_id</c>, <c>created_at</c>, <c>updated_at</c>, <c>last_activity_at</c>
/// (исходник <c>UserApiController::list</c>). Фильтр по любому другому полю сервер молча
/// выбрасывает и отдаёт ПОЛНЫЙ список без пометки о том, что условие не применено (замерено:
/// <c>?filter[nosuchfield]=1</c> вернул всех). Это та же ловушка, что у MantisBT с усечением
/// <c>config?option[]=</c>: тихая выдача не того, что просили. Поэтому здесь заведены именно
/// поддерживаемые поля, а свободного словаря нет.
/// </remarks>
public sealed class BookStackUserListQuery
{
    /// <summary>
    /// Сколько записей вернуть. Умолчание установки 100, потолок 500 (переменные окружения
    /// <c>API_DEFAULT_ITEM_COUNT</c> и <c>API_MAX_ITEM_COUNT</c>).
    /// </summary>
    /// <remarks>
    /// Выход за границы НЕ ошибка: значение молча зажимается. Замерено: <c>count=0</c> вернул одну
    /// запись, <c>count=1000</c> отдал не более пятисот. Полное число доступных записей приходит
    /// в <c>total</c> ответа, по нему и надо решать, долистывать ли.
    /// </remarks>
    public int? Count { get; set; }

    /// <summary>Сколько записей пропустить. Нумерация с нуля.</summary>
    public int? Offset { get; set; }

    /// <summary>
    /// Поле сортировки. Минус впереди означает по убыванию, например <c>-id</c> (проверено).
    /// Неизвестное имя молча заменяется первым полем списка.
    /// </summary>
    public string? Sort { get; set; }

    /// <summary>Точное совпадение почты (<c>filter[email]</c>). Проверено вживую.</summary>
    public string? Email { get; set; }

    /// <summary>
    /// Совпадение почты по образцу (<c>filter[email:like]</c>). Образец задаётся в синтаксисе SQL
    /// <c>LIKE</c>, то есть подстрока пишется как <c>%corp.lan</c>. Проверено вживую.
    /// </summary>
    public string? EmailLike { get; set; }

    /// <summary>
    /// Точное совпадение внешнего идентификатора (<c>filter[external_auth_id]</c>): способ найти
    /// учётку по <c>sub</c> от провайдера SSO. Проверено вживую.
    /// </summary>
    public string? ExternalAuthId { get; set; }

    /// <summary>Точное совпадение отображаемого имени (<c>filter[name]</c>).</summary>
    public string? Name { get; set; }

    /// <summary>Совпадение имени по образцу <c>LIKE</c> (<c>filter[name:like]</c>).</summary>
    public string? NameLike { get; set; }

    /// <summary>Точное совпадение короткого имени (<c>filter[slug]</c>).</summary>
    public string? Slug { get; set; }

    /// <summary>Собирает строку запроса из заполненных полей.</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                parts.Add($"{key}={Uri.EscapeDataString(value)}");
        }

        Add("count", Count?.ToString());
        Add("offset", Offset?.ToString());
        Add("sort", Sort);
        Add("filter[email]", Email);
        Add("filter[email:like]", EmailLike);
        Add("filter[external_auth_id]", ExternalAuthId);
        Add("filter[name]", Name);
        Add("filter[name:like]", NameLike);
        Add("filter[slug]", Slug);

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}

/// <summary>Мелочь для сборки строк запроса списков, у которых своей модели параметров нет.</summary>
internal static class BookStackQuery
{
    /// <summary>
    /// Собирает строку запроса из пар «имя и значение», пропуская пустые.
    /// </summary>
    /// <remarks>
    /// Квадратные скобки в именах фильтров НЕ кодируются намеренно: именно в таком виде запросы
    /// проверены на стенде, и в таком виде их разбирает PHP. Кодируются только значения.
    /// </remarks>
    public static string Build(params (string Key, string? Value)[] pairs)
    {
        var builder = new StringBuilder();

        foreach (var (key, value) in pairs)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            builder.Append(builder.Length == 0 ? '?' : '&')
                   .Append(key)
                   .Append('=')
                   .Append(Uri.EscapeDataString(value));
        }

        return builder.ToString();
    }
}
