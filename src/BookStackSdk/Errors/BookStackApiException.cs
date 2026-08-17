using System.Net;

namespace BookStackSdk.Errors;

/// <summary>
/// Ошибка вызова BookStack REST API.
/// </summary>
/// <remarks>
/// В отличие от MantisBT, отдельного кода приложения тут нет: <c>code</c> в теле повторяет
/// HTTP-статус (замерено на 401, 404, 422 и 429). Единственное, что несёт сведения сверх статуса,
/// это <see cref="ValidationErrors"/>, поэтому имена полей вынесены в текст сообщения: без них
/// «The given data was invalid» не говорит ничего.
/// <para>
/// Тело сохраняется целиком (<see cref="RawBody"/>) и не чистится. Решение о том, что показывать
/// человеку, принимает тот, кто перепубликовывает текст, а не транспорт.
/// </para>
/// </remarks>
public sealed class BookStackApiException : Exception
{
    public BookStackApiException(
        HttpStatusCode statusCode,
        BookStackError? error,
        string? rawBody,
        string? reasonPhrase = null)
        : base(BuildMessage(statusCode, error, reasonPhrase))
    {
        StatusCode = statusCode;
        Error = error;
        RawBody = rawBody;
        ReasonPhrase = reasonPhrase;
    }

    /// <summary>HTTP-статус ответа.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>Разобранное тело ошибки. <c>null</c>, если тела не было или оно не разобралось.</summary>
    public BookStackError? Error { get; }

    /// <summary>
    /// Сырое тело ответа. Может быть не-JSON: перед приложением стоит nginx, и его страницы
    /// (502, 504, отказ по размеру тела) приходят HTML-ом.
    /// </summary>
    public string? RawBody { get; }

    /// <summary>Статусная строка ответа. Носитель текста, когда тела нет вовсе.</summary>
    public string? ReasonPhrase { get; }

    /// <summary>
    /// Претензии валидации по полям, если отказ был по ней.
    /// </summary>
    /// <remarks>
    /// Помнить про двойное значение: «поле обязательно» приходит и тогда, когда поле отправлено,
    /// но тело не разобрано целиком (испорченная кодировка, BOM, отсутствие <c>Content-Type</c>).
    /// Замерено на стенде 17.08.2026, подробности в <see cref="BookStackError.Validation"/>.
    /// </remarks>
    public IReadOnlyDictionary<string, List<string>>? ValidationErrors => Error?.Validation;

    private static string BuildMessage(HttpStatusCode statusCode, BookStackError? error, string? reason)
    {
        var head = $"Вызов BookStack API завершился с кодом {(int)statusCode} ({statusCode})";

        if (error is not null && !string.IsNullOrWhiteSpace(error.Message))
        {
            // Код в теле дублирует статус, поэтому в текст он не выносится: повторять одно число
            // дважды значит делать сообщение длиннее, не делая его информативнее.
            if (error.Validation is { Count: > 0 })
            {
                var fields = string.Join(
                    "; ",
                    error.Validation.Select(p => $"{p.Key}: {string.Join(", ", p.Value)}"));
                return $"{head}: {error.Message} ({fields})";
            }

            return $"{head}: {error.Message}";
        }

        return string.IsNullOrWhiteSpace(reason) ? head + "." : $"{head}: {reason}";
    }
}
