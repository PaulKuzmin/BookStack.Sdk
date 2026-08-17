namespace BookStackSdk.Models;

/// <summary>
/// Книга BookStack: контейнер глав и страниц, единица прав доступа и единица выгрузки.
/// </summary>
/// <remarks>
/// Как и у страницы, набор заполненных полей зависит от маршрута, поэтому все они nullable.
/// Замер на стенде 17.08.2026:
/// <list type="bullet">
/// <item><c>GET /api/books/{id}</c> отдаёт <c>description_html</c>, <c>tags</c>, <c>contents</c>
/// и <c>shelves</c>;</item>
/// <item><c>GET /api/books</c> не отдаёт ничего из этого, только плоские поля и укороченную
/// обложку;</item>
/// <item>книги внутри полки (<c>books</c> в ответе <c>GET /api/shelves/{id}</c>) приходят без
/// <c>tags</c>, <c>cover</c> и <c>contents</c>, зато с <c>image_id</c>.</item>
/// </list>
/// </remarks>
public sealed class BookStackBook
{
    /// <summary>Идентификатор книги.</summary>
    public int? Id { get; set; }

    /// <summary>Вид сущности, <c>book</c>. В списке этого поля нет.</summary>
    public string? Type { get; set; }

    /// <summary>Название книги.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Слаг книги, часть адреса. Пересчитывается при переименовании, см.
    /// <see cref="BookStackPage.Slug"/>.
    /// </summary>
    public string? Slug { get; set; }

    /// <summary>Описание книги простым текстом.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// Описание книги в HTML.
    /// </summary>
    /// <remarks>
    /// Два поля описания это не дубль: при записи они спорят, и спор решается в пользу HTML.
    /// В исходнике <c>BaseRepo::updateDescription()</c> сначала проверяется
    /// <c>isset($input['description_html'])</c>, и только если его нет, берётся
    /// <c>description</c>. То есть послать оба и ждать, что применится простой текст, нельзя.
    /// Обратный ход достраивается сам: замер показал, что после записи одного лишь
    /// <c>description: "Опись"</c> сервер вернул <c>description_html: "&lt;p&gt;Опись&lt;/p&gt;"</c>.
    /// В списке книг это поле не приходит: модель книги прячет его по умолчанию и открывает только
    /// при чтении, создании и обновлении.
    /// </remarks>
    public string? DescriptionHtml { get; set; }

    /// <summary>Когда книга создана. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда книга изменена. UTC.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Кто создал. Форма ответа двоякая (число или объект) и зависит от маршрута, разбор замеров
    /// в <see cref="BookStackPage.CreatedBy"/>.
    /// </summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто изменил последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Кто владеет книгой.</summary>
    public BookStackUserRef? OwnedBy { get; set; }

    /// <summary>Страница-шаблон, подставляемая в новые страницы этой книги.</summary>
    public int? DefaultTemplateId { get; set; }

    /// <summary>
    /// Идентификатор картинки обложки.
    /// </summary>
    /// <remarks>
    /// Приходит в списке и во вложенных наборах, где самой обложки может не быть развёрнутой.
    /// У полки такого поля нет вовсе: модель <c>Bookshelf</c> прячет <c>image_id</c>, а модель
    /// <c>Book</c> нет (проверено в исходниках, подтверждено ответами).
    /// </remarks>
    public int? ImageId { get; set; }

    /// <summary>
    /// Правило автосортировки содержимого книги.
    /// </summary>
    /// <remarks>
    /// Через API не задаётся: ни <c>POST /api/books</c>, ни <c>PUT /api/books/{id}</c> такого поля
    /// в правилах проверки не имеют (сверено с живой докой <c>/api/docs.json</c> и с исходником
    /// <c>BookApiController::rules()</c>). В ответе оно есть, поэтому и в модели есть, но менять
    /// его надо в интерфейсе BookStack.
    /// </remarks>
    public int? SortRuleId { get; set; }

    /// <summary>
    /// Обложка книги: та же запись картинки галереи, что и в остальном SDK.
    /// </summary>
    /// <remarks>
    /// Полнота записи зависит от маршрута, и это замерено на стенде 17.08.2026 на книге с
    /// поставленной обложкой:
    /// <list type="bullet">
    /// <item>в списке книг приходят только три поля, <c>id</c>, <c>name</c> и <c>url</c>. Так и
    /// написано в исходнике: <c>-&gt;with(['cover:id,name,url'])</c>;</item>
    /// <item>при чтении, создании и обновлении приходит запись целиком, включая <c>path</c>,
    /// <c>type</c> (у обложки книги это <c>cover_book</c>) и <c>uploaded_to</c>. Причина в
    /// исходнике: модель <c>app/Uploads/Image.php</c> объявляет <c>protected $hidden = []</c>.</item>
    /// </list>
    /// Полей <c>thumbs</c> и <c>content</c> у обложки не бывает: их достраивают только маршруты
    /// галереи картинок. То есть <c>null</c> в них означает «этот маршрут их не отдаёт».
    /// <para>
    /// Сама постановка обложки сюда не входит: она уходит многочастным телом с полем
    /// <c>_method=PUT</c>, см. <see cref="Internal.BookStackMultipart"/>.
    /// </para>
    /// </remarks>
    public BookStackImage? Cover { get; set; }

    /// <summary>
    /// Оглавление: главы и страницы верхнего уровня в том порядке, в каком их показывает BookStack.
    /// </summary>
    /// <remarks>
    /// Приходит ТОЛЬКО при чтении одиночной книги. Ни в списке книг, ни при создании, ни при
    /// обновлении его нет (замерено), поэтому после создания книги оглавление надо дочитывать
    /// отдельным запросом. Что именно лежит в элементе, см. <see cref="BookStackContentRef"/>.
    /// </remarks>
    public List<BookStackContentRef>? Contents { get; set; }

    /// <summary>
    /// Полки, на которых стоит книга. Приходит только при чтении одиночной книги.
    /// </summary>
    /// <remarks>
    /// Отдаются укороченно: идентификатор, имя и слаг (в исходнике
    /// <c>$query-&gt;select(['id', 'name', 'slug'])</c>), и только те полки, которые видны текущему
    /// токену. То есть пустой список значит «видимых полок нет», а не «книга ни на одной полке».
    /// </remarks>
    public List<BookStackShelf>? Shelves { get; set; }

    /// <summary>Теги книги. Приходят при чтении, создании и обновлении, в списке их нет.</summary>
    public List<BookStackTag>? Tags { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"книга #{Id} {Name}";
}
