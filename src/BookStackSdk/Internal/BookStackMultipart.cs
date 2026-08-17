using System.Text;

namespace BookStackSdk.Internal;

/// <summary>
/// Сборщик тела <c>multipart/form-data</c> для маршрутов BookStack, принимающих файлы: обложки книг
/// и полок, картинки галереи, вложения, загрузка импорта.
/// </summary>
/// <remarks>
/// ВАЖНО: обновление файлового поля уходит методом <c>POST</c> с полем <c>_method=PUT</c> внутри
/// тела, а не настоящим <c>PUT</c>. Подмена метода это штатный приём Laravel, на котором стоит
/// BookStack, и работает он одинаково везде. Настоящий <c>PUT</c> работает не везде, и вот чем это
/// подтверждено.
/// <para>
/// Исходник <c>vendor/symfony/http-foundation/Request.php</c> в контейнере стенда, метод
/// <c>createFromGlobals()</c>: для методов <c>PUT</c>, <c>DELETE</c>, <c>PATCH</c> и <c>QUERY</c>
/// на PHP 8.4 и новее вызывается <c>request_parse_body()</c>, который многочастное тело разбирает,
/// а на PHP старше 8.4 разбирается только <c>application/x-www-form-urlencoded</c>, а файлы берутся
/// из <c>$_FILES</c>, который PHP заполняет ТОЛЬКО для <c>POST</c>. То есть на старом PHP файл при
/// настоящем <c>PUT</c> пропадает молча, ответ при этом 200.
/// </para>
/// <para>
/// Замер на стенде 17.08.2026 (BookStack 26.05.3, PHP 8.5.9) это подтверждает с той стороны:
/// там настоящий <c>PUT</c> с файлом ПРОХОДИТ, обложка встаёт. То есть проба на стенде НЕ докажет
/// поломку, а на боевой установке help.altway.pro она записана как случившаяся (см. скилл
/// bookstack в AltWayDocs). Оба пути ведут к одному выводу: подмена метода работает при любой
/// версии PHP, настоящий <c>PUT</c> только при новой, поэтому здесь всегда подмена.
/// </para>
/// </remarks>
public sealed class BookStackMultipart
{
    /// <summary>Имя поля подмены метода. Разбирается Laravel до маршрутизации.</summary>
    private const string MethodOverrideField = "_method";

    private readonly MultipartFormDataContent _content = new();

    private BookStackMultipart(string? methodOverride)
    {
        // Поле подмены кладётся ПЕРВЫМ, до всех прочих. Порядок частей формально не важен, но
        // именно в таком порядке проверено вживую, и держать проверенный порядок дешевле, чем
        // однажды выяснять, что чей-то разбор потокового тела успел уйти в маршрут раньше.
        if (methodOverride is not null)
            AddField(MethodOverrideField, methodOverride);
    }

    /// <summary>Создание сущности: обычный <c>POST</c>, подмены метода нет.</summary>
    public static BookStackMultipart ForCreate() => new(methodOverride: null);

    /// <summary>
    /// Обновление сущности: тот же <c>POST</c>, но с полем <c>_method=PUT</c>, чтобы BookStack
    /// разобрал запрос как обновление. Подробности в описании класса.
    /// </summary>
    public static BookStackMultipart ForUpdate() => new(methodOverride: "PUT");

    /// <summary>
    /// Добавляет текстовое поле формы.
    /// </summary>
    /// <remarks>
    /// Кодировка прибита к UTF-8 по той же причине, что и у JSON: испорченные байты BookStack
    /// возвращает как «поле обязательно», а не как ошибку разбора. Часть при этом получает
    /// <c>Content-Type: text/plain; charset=utf-8</c>, и это проверено вживую: имя книги с
    /// кириллицей, отправленное такой частью, сохраняется целым.
    /// </remarks>
    public BookStackMultipart AddField(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _content.Add(new StringContent(value ?? string.Empty, Encoding.UTF8), name);
        return this;
    }

    /// <summary>
    /// Добавляет файл.
    /// </summary>
    /// <param name="name">Имя поля формы, которого ждёт маршрут (например, <c>image</c> или <c>file</c>).</param>
    /// <param name="fileName">Имя файла в части. Служебное, см. примечание.</param>
    /// <param name="content">Содержимое файла.</param>
    /// <param name="contentType">
    /// Тип содержимого. Если не задан, уходит <c>application/octet-stream</c>: угадывать тип по
    /// расширению здесь некому и незачем, вызывающий знает его точно.
    /// </param>
    /// <remarks>
    /// Имя файла остаётся служебным. Видимое имя картинки задаётся отдельным полем <c>name</c>
    /// того же запроса, и полагаться на имя файла как на подпись не стоит: не-ASCII имена .NET
    /// кодирует в форму MIME-слова, и что из этого сохранит чужая сторона, мы не проверяли.
    /// </remarks>
    public BookStackMultipart AddFile(string name, string fileName, byte[] content, string? contentType = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(content);

        var part = new ByteArrayContent(content);
        part.Headers.TryAddWithoutValidation(
            "Content-Type",
            string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType);

        _content.Add(part, name, fileName);
        return this;
    }

    /// <summary>
    /// Отдаёт собранное тело. Владение переходит запросу: <see cref="HttpRequestMessage"/> закроет
    /// его вместе с собой, поэтому повторно отправлять один и тот же экземпляр нельзя.
    /// </summary>
    public MultipartFormDataContent Build() => _content;
}
