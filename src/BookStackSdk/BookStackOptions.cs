using System.ComponentModel.DataAnnotations;

namespace BookStackSdk;

/// <summary>
/// Настройки клиента BookStack REST API.
/// </summary>
public sealed class BookStackOptions
{
    /// <summary>Базовый адрес установки без хвостового слэша, например <c>http://localhost:6875</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Первая половина токена: идентификатор (Token ID из профиля пользователя BookStack).
    /// </summary>
    /// <remarks>
    /// ВАЖНО: половинки живут отдельными полями намеренно, и склеиваются ровно в одном месте,
    /// в <see cref="BuildAuthorizationHeaderValue"/>. Причина замерена на стенде 17.08.2026:
    /// при перестановке половинок местами BookStack отвечает 401 «No matching API token was found
    /// for the provided authorization token», то есть тем же текстом, что и на просроченный,
    /// отозванный или чужой токен. Ответ не подсказывает, что перепутан порядок, а не сам токен,
    /// и на разбор такой ошибки уходит день. Одно поле с уже готовой строкой это ровно тот случай,
    /// когда ошибку некому поймать: обе половинки выглядят одинаково (32 символа base62).
    /// </remarks>
    [Required(AllowEmptyStrings = false)]
    public string TokenId { get; set; } = string.Empty;

    /// <summary>Вторая половина токена: секрет (Token Secret, показывается только при выпуске).</summary>
    [Required(AllowEmptyStrings = false)]
    public string TokenSecret { get; set; } = string.Empty;

    /// <summary>
    /// Таймаут HTTP-запроса. Дефолт <see cref="HttpClient"/> (100 секунд) для прикладного кода
    /// слишком долог: зависший портал держал бы наш запрос.
    /// </summary>
    /// <remarks>
    /// Значение вынесено в настройку не только ради стенда: выгрузка книги в PDF считается на лету
    /// и живёт заметно дольше обычного чтения (замер на стенде: 453 килобайта PDF на пустую книгу).
    /// Для больших книг 30 секунд может не хватить, и это решение вызывающей стороны, а не наше.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Сколько раз повторять запрос при <c>429</c>, <c>5xx</c> и сетевых сбоях. 0 (ноль) без повторов.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>База экспоненциальной задержки между повторами.</summary>
    /// <remarks>
    /// Расчётная задержка нужна редко: на <c>429</c> BookStack сам присылает <c>Retry-After</c>
    /// и <c>X-RateLimit-Reset</c> (замерено), и они имеют приоритет, см.
    /// <see cref="Http.BookStackRetryHandler"/>.
    /// </remarks>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Базовый адрес REST API с хвостовым слэшем.</summary>
    public string ResolveApiBaseUrl() => BaseUrl.TrimEnd('/') + "/api/";

    /// <summary>
    /// Единственное место, где половинки токена превращаются в значение заголовка
    /// <c>Authorization</c>. Формат: <c>Token {id}:{secret}</c>.
    /// </summary>
    /// <remarks>
    /// Схема здесь есть и она обязательна: замерено, что <c>Bearer {id}:{secret}</c> тем же токеном
    /// даёт 401, а <c>Token {id}:{secret}</c> даёт 200. Это отличие от MantisBT, где токен уходит
    /// вообще без схемы. Метод намеренно не принимает аргументов: любая перегрузка вида
    /// «собери из двух строк» открыла бы дорогу вызову с переставленными местами половинками,
    /// а именно от этого поля и разведены.
    /// </remarks>
    public string BuildAuthorizationHeaderValue() => $"Token {TokenId}:{TokenSecret}";
}
