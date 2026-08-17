using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookStackSdk.Internal;

/// <summary>Единые настройки сериализации и единственная точка сборки тела запроса.</summary>
internal static class BookStackJson
{
    /// <summary>
    /// snake_case имена, null не отправляются, числа читаются и из строк.
    /// </summary>
    /// <remarks>
    /// <see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> здесь не про безопасность, а про
    /// кириллицу: со штатным кодировщиком русский текст уезжает эскейп-последовательностями, и
    /// сравнить отправленное тело с ожидаемым глазами становится невозможно. На приём это не влияет
    /// (BookStack сам отвечает эскейпленным ASCII, замерено), но на диагностику влияет сильно.
    /// <para>
    /// Про <see cref="JsonIgnoreCondition.WhenWritingNull"/>: у BookStack есть маршруты, где явный
    /// <c>null</c> значит «снять значение» (обложка книги снимается полем <c>image: null</c>, так
    /// написано в исходнике <c>BookApiController</c>). Такие поля должны помечаться в модели
    /// атрибутом <c>[JsonIgnore(Condition = JsonIgnoreCondition.Never)]</c> поштучно: снимать
    /// правило целиком нельзя, иначе каждое частичное обновление начнёт затирать неупомянутые поля.
    /// </para>
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// Собирает тело запроса: UTF-8 без BOM плюс явная кодировка в <c>Content-Type</c>.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: это единственное место, где рождается тело JSON-запроса, и сделано так ради одного
    /// замера. На стенде 17.08.2026 создание книги с русским именем проверено четырьмя способами:
    /// <list type="bullet">
    /// <item>байты UTF-8, <c>Content-Type: application/json; charset=utf-8</c>: 200, имя целое;</item>
    /// <item>байты UTF-8, <c>Content-Type: application/json</c> без кодировки: тоже 200;</item>
    /// <item>байты cp1251 при заявленном <c>charset=utf-8</c>: 422 «The name field is required»;</item>
    /// <item>байты UTF-8 с BOM перед первой скобкой: 422 «The name field is required»;</item>
    /// <item>тело без заголовка <c>Content-Type</c> вовсе: 422 «The name field is required».</item>
    /// </list>
    /// То есть сервер не требует параметра <c>charset</c>, но ЛЮБАЯ порча байтов или потеря типа
    /// содержимого маскируется под «вы забыли поле», а не под ошибку разбора. Отличить одно от
    /// другого по ответу нельзя, и именно поэтому кодировка прибита здесь, а не оставлена на
    /// усмотрение вызывающего кода: единственный способ не получить эту ошибку, это не иметь места,
    /// где её можно совершить.
    /// <para>
    /// <see cref="StringContent"/> с <see cref="Encoding.UTF8"/> преамбулу (BOM) не пишет, только
    /// проставляет параметр <c>charset=utf-8</c>. Замер выше показывает, что если бы писала, тело
    /// молча перестало бы доезжать.
    /// </para>
    /// </remarks>
    public static StringContent CreateContent<T>(T body)
        => new(JsonSerializer.Serialize(body, Options), Encoding.UTF8, "application/json");
}
