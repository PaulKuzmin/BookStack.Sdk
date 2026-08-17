using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace BookStackSdk.Tests.Infrastructure;

/// <summary>
/// Заглушка транспорта: перехватывает исходящие запросы и отдаёт заранее заданный ответ.
/// </summary>
/// <remarks>
/// ВАЖНО, чем эта заглушка отличается от обычной: тело запоминается СЫРЫМИ БАЙТАМИ, а не строкой.
/// Причина замерена на стенде 17.08.2026 и стоила питоновской обёртке дня разбора: тело в чужой
/// кодировке BookStack принимает со статусом 422 «The name field is required», то есть жалуется на
/// пропажу поля, а не на разбор. Проверка вида «в отправленной строке есть нужное имя» такую порчу
/// НЕ ловит: <see cref="HttpContent.ReadAsStringAsync()"/> раскодирует байты по заголовку
/// <c>Content-Type</c> и вернёт целую строку даже тогда, когда на провод ушла кодировка, которой
/// сервер не ждёт. Отличить одно от другого можно только байтами, поэтому они и хранятся.
/// <para>
/// По той же причине заголовки снимаются через <c>NonValidated</c>: разбор
/// <see cref="HttpRequestHeaders.Authorization"/> расщепил бы значение на схему и параметр и
/// проглотил бы лишние пробелы, а проверять надо ровно ту строку, которая уходит на провод.
/// </para>
/// </remarks>
public sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, byte[], HttpResponseMessage> _responder;

    /// <summary>Перехваченные запросы в порядке отправки.</summary>
    public List<CapturedRequest> Requests { get; } = [];

    public StubHttpMessageHandler(Func<HttpRequestMessage, byte[], HttpResponseMessage> responder)
        => _responder = responder;

    /// <summary>Всегда отвечает одним и тем же телом JSON с указанным статусом.</summary>
    public static StubHttpMessageHandler Json(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new((_, _) => JsonResponse(body, status));

    /// <summary>Отвечает пустым телом с указанным статусом: так выглядит удаление (204).</summary>
    public static StubHttpMessageHandler Empty(HttpStatusCode status = HttpStatusCode.NoContent)
        => new((_, _) => new HttpResponseMessage(status) { Content = new ByteArrayContent([]) });

    /// <summary>
    /// Отвечает телами по очереди: первый вызов первым телом и так далее. Последнее тело
    /// повторяется, если вызовов окажется больше, чем заготовок.
    /// </summary>
    public static StubHttpMessageHandler Sequence(params (string Body, HttpStatusCode Status)[] steps)
    {
        var call = 0;
        return new StubHttpMessageHandler((_, _) =>
        {
            var step = steps[Math.Min(call, steps.Length - 1)];
            call++;
            return JsonResponse(step.Body, step.Status);
        });
    }

    /// <summary>Готовый ответ JSON. Кодировка ответа тут не проверяется, она забота сервера.</summary>
    public static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        Requests.Add(new CapturedRequest(
            request.Method,
            request.RequestUri,
            body,
            ReadRawHeader(request, "Authorization"),
            request.Content?.Headers.ContentType,
            SnapshotHeaders(request)));

        return _responder(request, body);
    }

    /// <summary>Значение заголовка ровно в том виде, в каком оно уходит, без разбора на части.</summary>
    private static string? ReadRawHeader(HttpRequestMessage request, string name)
        => request.Headers.NonValidated.TryGetValues(name, out var values) ? values.ToString() : null;

    private static IReadOnlyDictionary<string, string> SnapshotHeaders(HttpRequestMessage request)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in request.Headers.NonValidated)
            headers[header.Key] = header.Value.ToString();

        if (request.Content is not null)
        {
            foreach (var header in request.Content.Headers.NonValidated)
                headers[header.Key] = header.Value.ToString();
        }

        return headers;
    }
}

/// <summary>Снимок перехваченного запроса.</summary>
/// <param name="Method">Метод запроса. Для обложек и вложений он обязан быть <c>POST</c>.</param>
/// <param name="Url">Полный адрес вместе со строкой запроса.</param>
/// <param name="Body">Тело сырыми байтами, ровно как оно уходит на провод.</param>
/// <param name="Authorization">Заголовок <c>Authorization</c> строкой, без разбора на схему и параметр.</param>
/// <param name="ContentType">Тип содержимого вместе с параметрами (<c>charset</c> у JSON, <c>boundary</c> у multipart).</param>
/// <param name="Headers">Все заголовки запроса и содержимого, сырыми значениями.</param>
public sealed record CapturedRequest(
    HttpMethod Method,
    Uri? Url,
    byte[] Body,
    string? Authorization,
    MediaTypeHeaderValue? ContentType,
    IReadOnlyDictionary<string, string> Headers)
{
    /// <summary>
    /// Тело, раскодированное как UTF-8.
    /// </summary>
    /// <remarks>
    /// Годится для разбора структуры (какое поле в каком месте), но НЕ годится как доказательство
    /// кодировки: любая порча байтов тут уже потеряна. Кодировку проверяйте через
    /// <see cref="ContainsBytes"/> и сравнение с <see cref="Body"/>.
    /// </remarks>
    public string BodyAsUtf8 => Encoding.UTF8.GetString(Body);

    /// <summary>Строка запроса вместе со знаком вопроса, либо пустая строка.</summary>
    public string Query => Url?.Query ?? string.Empty;

    /// <summary>Путь адреса без строки запроса.</summary>
    public string Path => Url?.AbsolutePath ?? string.Empty;

    /// <summary>Есть ли в теле такая последовательность байтов подряд.</summary>
    /// <remarks>
    /// Именно подряд, а не «эти байты где-то встречаются»: разорванная последовательность означала
    /// бы, что на провод ушло не то, что мы собирали.
    /// </remarks>
    public bool ContainsBytes(byte[] needle)
    {
        if (needle.Length == 0 || needle.Length > Body.Length)
            return false;

        for (var start = 0; start <= Body.Length - needle.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < needle.Length && matched; i++)
                matched = Body[start + i] == needle[i];

            if (matched)
                return true;
        }

        return false;
    }

    /// <summary>Есть ли в теле байты этого текста в кодировке UTF-8.</summary>
    public bool ContainsUtf8(string text) => ContainsBytes(Encoding.UTF8.GetBytes(text));
}
