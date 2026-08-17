namespace BookStackSdk.Models;

/// <summary>
/// Тело создания и обновления книги (<c>POST /api/books</c> и <c>PUT /api/books/{id}</c>).
/// </summary>
/// <remarks>
/// Тип один на оба действия потому, что набор полей у них совпадает буква в букву. Сверено с
/// исходником <c>BookApiController::rules()</c>: разница только в строгости имени, на создании
/// <c>required</c>, на обновлении <c>min:1</c>. Заводить второй тип ради одного этого различия
/// значило бы дублировать одиннадцать строк документации, а различие всё равно проверяет сервер.
/// <para>
/// Отсюда все поля nullable: на обновлении неупомянутое поле остаётся как было (обновление
/// частичное, несмотря на <c>PUT</c>), а на создании отсутствие имени сервер отвергнет сам.
/// </para>
/// <para>
/// ВАЖНО: поля <c>image</c> тут нет. Обложка ставится не JSON-ом, а многочастным телом с полем
/// <c>_method=PUT</c>, потому что PHP не разбирает файлы при настоящем <c>PUT</c>. Это отдельный
/// путь, см. <see cref="Internal.BookStackMultipart"/>.
/// </para>
/// </remarks>
public sealed class BookStackBookCreate
{
    /// <summary>
    /// Название книги. На создании обязательно, ограничение длины 255 символов.
    /// </summary>
    /// <remarks>
    /// Про двойной смысл отказа «The name field is required» см.
    /// <see cref="BookStackPageCreate.Name"/>.
    /// </remarks>
    public string? Name { get; set; }

    /// <summary>
    /// Описание простым текстом. Ограничение длины 1900 символов.
    /// </summary>
    /// <remarks>
    /// Если задать только его, сервер сам соберёт HTML-версию (замер: <c>description: "Опись"</c>
    /// вернулось как <c>description_html: "&lt;p&gt;Опись&lt;/p&gt;"</c>).
    /// </remarks>
    public string? Description { get; set; }

    /// <summary>
    /// Описание в HTML. Ограничение длины 2000 символов.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: при отправке обоих полей описания выигрывает ЭТО, а <see cref="Description"/> молча
    /// не применяется. В исходнике <c>BaseRepo::updateDescription()</c> сначала проверяется
    /// <c>isset($input['description_html'])</c>, и ветка с простым текстом до выполнения не доходит.
    /// Замер на стенде 17.08.2026: книга, обновлённая телом
    /// <c>{"description":"простой текст","description_html":"&lt;p&gt;разметка&lt;/p&gt;"}</c>,
    /// вернулась с <c>description: "разметка"</c>, то есть присланный простой текст пропал совсем,
    /// а на его месте оказался текст, вынутый сервером из разметки. Отказа при этом нет, ответ 200.
    /// Разметка вдобавок фильтруется сервером (<c>HtmlDescriptionFilter</c>), то есть вернуться
    /// может не то, что послали.
    /// </remarks>
    public string? DescriptionHtml { get; set; }

    /// <summary>Страница-шаблон для новых страниц этой книги.</summary>
    public int? DefaultTemplateId { get; set; }

    /// <summary>
    /// Теги книги.
    /// </summary>
    /// <remarks>
    /// Те же три состояния, что и у страницы: <c>null</c> не трогает, пустой список снимает все,
    /// непустой замещает целиком. См. <see cref="BookStackTag"/>.
    /// </remarks>
    public List<BookStackTag>? Tags { get; set; }
}
