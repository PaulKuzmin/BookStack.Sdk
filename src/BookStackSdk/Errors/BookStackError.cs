using System.Text.Json.Serialization;

namespace BookStackSdk.Errors;

/// <summary>
/// Конверт ошибки BookStack: <c>{"error": {…}}</c>. Тело ошибки всегда лежит под этим ключом,
/// а не в корне, в отличие от MantisBT.
/// </summary>
/// <remarks>
/// Замерено на стенде 17.08.2026 четырьмя разными отказами (401 без токена, 404 на чужой id,
/// 422 на пустое тело, 429 на исчерпанный лимит): форма одна и та же, обёртка присутствует всегда.
/// </remarks>
public sealed class BookStackErrorEnvelope
{
    /// <summary>Само тело ошибки.</summary>
    [JsonPropertyName("error")]
    public BookStackError? Error { get; set; }
}

/// <summary>
/// Тело ошибки BookStack: <c>{"message": "...", "code": 422, "validation": {…}}</c>.
/// </summary>
public sealed class BookStackError
{
    /// <summary>Сообщение на английском.</summary>
    [JsonPropertyName("message")]
    public string? Message { get; set; }

    /// <summary>
    /// Код ошибки.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: это НЕ отдельная нумерация приложения, а повторение HTTP-статуса. Замерено:
    /// 401 приходит с <c>code: 401</c>, 404 с <c>code: 404</c>, 422 с <c>code: 422</c>,
    /// 429 с <c>code: 429</c>. Поэтому здесь нет справочника констант вроде того, что есть
    /// у MantisBT с его кодами 811 и 1201: заводить их значило бы обещать различение, которого
    /// в ответе нет. Разбирать по существу надо <see cref="Validation"/>, а не это число.
    /// </remarks>
    [JsonPropertyName("code")]
    public int? Code { get; set; }

    /// <summary>
    /// Разбор отказа валидации: имя поля и список претензий к нему.
    /// </summary>
    /// <remarks>
    /// Единственная часть ответа, называющая ПОЛЕ. Замер: <c>POST /api/books</c> с телом <c>{}</c>
    /// отдаёт <c>{"error":{"message":"The given data was invalid.","validation":{"name":["The name
    /// field is required."]},"code":422}}</c>. Ключи приходят как есть, именами полей API, поэтому
    /// это словарь, а не типизированная модель: набор полей свой у каждого маршрута.
    /// <para>
    /// Отдельная ловушка: тот же 422 «field is required» приходит и тогда, когда поле было
    /// отправлено, но тело не разобралось. Замерено на стенде: тело в cp1251 при заявленном
    /// <c>charset=utf-8</c>, тело с BOM перед первой скобкой и тело вообще без заголовка
    /// <c>Content-Type</c> дают ровно этот ответ. То есть «поле обязательно» может означать
    /// «тело не дошло целиком», см. <see cref="Internal.BookStackJson"/>.
    /// </para>
    /// </remarks>
    [JsonPropertyName("validation")]
    public Dictionary<string, List<string>>? Validation { get; set; }

    /// <inheritdoc />
    public override string ToString()
    {
        var head = Code is > 0 ? $"[{Code}] {Message}" : Message ?? string.Empty;
        if (Validation is not { Count: > 0 })
            return head;

        var fields = string.Join("; ", Validation.Select(p => $"{p.Key}: {string.Join(", ", p.Value)}"));
        return $"{head} ({fields})";
    }
}
