using BookStackSdk.Internal;
using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Загрузки: картинки галереи, вложения страниц и обложки книг и полок.
/// </summary>
/// <remarks>
/// Три разных маршрута собраны в один интерфейс не по формальному признаку, а потому что у них
/// общая механика: тело уходит многочастным, а обновление файлового поля идёт методом <c>POST</c>
/// с полем <c>_method=PUT</c> внутри тела. Обоснование подмены метода целиком лежит в
/// <see cref="BookStackMultipart"/>: коротко, на PHP старше 8.4 файл при настоящем <c>PUT</c>
/// пропадает молча, а ответ при этом 200.
/// <para>
/// Чего в API BookStack нет и чего поэтому нет здесь: отдельного маршрута загрузки обложки.
/// Обложка ставится обновлением самой книги или полки, поэтому методы обложек живут тут (это
/// загрузка файла), а снятие обложки живёт в обновлении книги полем <c>image: null</c>, то есть
/// в содержимом.
/// </para>
/// </remarks>
public interface IBookStackUploadsApi
{
    // ---- Картинки галереи ----

    /// <summary>
    /// Список картинок (<c>GET /api/image-gallery</c>).
    /// </summary>
    /// <param name="count">Сколько вернуть. Умолчание установки 100, потолок 500, выход за границы молча зажимается.</param>
    /// <param name="offset">Сколько пропустить.</param>
    /// <param name="sort">Поле сортировки, минус впереди означает по убыванию.</param>
    /// <param name="uploadedTo">Оставить только картинки указанной СТРАНИЦЫ.</param>
    /// <param name="type">Оставить только указанный вид, см. <see cref="BookStackImageType"/>.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// В списке нет ни <c>thumbs</c>, ни <c>content</c>, а <c>created_by</c> приходит числом. Если
    /// нужны уменьшенные копии, читайте картинку поштучно, см. <see cref="GetImageAsync"/>.
    /// Фильтровать можно только по полям списка (<c>id</c>, <c>name</c>, <c>url</c>, <c>path</c>,
    /// <c>type</c>, <c>uploaded_to</c>, <c>created_by</c>, <c>updated_by</c>, <c>created_at</c>,
    /// <c>updated_at</c>): фильтр по любому другому полю сервер молча выбрасывает.
    /// </remarks>
    Task<BookStackPage<BookStackImage>> ListImagesAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        int? uploadedTo = null,
        string? type = null,
        CancellationToken ct = default);

    /// <summary>
    /// Чтение картинки (<c>GET /api/image-gallery/{id}</c>). Отдаёт полную модель с адресами
    /// уменьшенных копий и готовой разметкой вставки.
    /// </summary>
    Task<BookStackImage?> GetImageAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Байты картинки (<c>GET /api/image-gallery/{id}/data</c>).
    /// </summary>
    /// <remarks>
    /// В отличие от выгрузок, тут сервер называет настоящий тип содержимого: замерено
    /// <c>Content-Type: image/png</c> и <c>Content-Disposition: inline; filename=...</c>.
    /// </remarks>
    Task<BookStackBinary> GetImageDataAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Байты картинки по её адресу (<c>GET /api/image-gallery/url/data?url=...</c>).
    /// </summary>
    /// <remarks>
    /// Нужен там, где на руках только адрес из тела страницы, а идентификатора нет. Адрес уходит
    /// закодированным параметром запроса.
    /// </remarks>
    Task<BookStackBinary> GetImageDataByUrlAsync(string url, CancellationToken ct = default);

    /// <summary>
    /// Загружает картинку в галерею (<c>POST /api/image-gallery</c>).
    /// </summary>
    /// <param name="uploadedToPageId">Идентификатор СТРАНИЦЫ, к которой привязывается картинка. Обязателен.</param>
    /// <param name="fileName">Имя файла в многочастном теле. Служебное.</param>
    /// <param name="content">Байты файла.</param>
    /// <param name="contentType">Тип содержимого, например <c>image/png</c>.</param>
    /// <param name="name">
    /// Видимое имя картинки. Если не задано, сервер подставит имя файла. Имя с кириллицей
    /// проверено вживую и сохраняется целым.
    /// </param>
    /// <param name="type">Вид: <see cref="BookStackImageType.Gallery"/> или <see cref="BookStackImageType.Drawio"/>.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// ВАЖНО: успешный ответ 200 НЕ означает, что картинка годная. Замерено 17.08.2026: файл,
    /// который GD не смог прочитать, загрузился с кодом 200, но с пустыми
    /// <see cref="BookStackImage.Thumbs"/> и с пустым адресом внутри готовой разметки. Проверять
    /// надо ответ, а не код, см. <see cref="BookStackImageThumbs"/>.
    /// </remarks>
    Task<BookStackImage?> UploadImageAsync(
        int uploadedToPageId,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        string type = BookStackImageType.Gallery,
        CancellationToken ct = default);

    /// <summary>
    /// Переименовывает картинку (<c>PUT /api/image-gallery/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Тело обычное, JSON: подмена метода тут не нужна, потому что файла в запросе нет. Проверено
    /// вживую, настоящий <c>PUT</c> с одним полем <c>name</c> проходит.
    /// </remarks>
    Task<BookStackImage?> RenameImageAsync(int id, string name, CancellationToken ct = default);

    /// <summary>
    /// Заменяет файл картинки, при желании заодно переименовывая её.
    /// </summary>
    /// <remarks>
    /// Уходит <c>POST</c> с полем <c>_method=PUT</c> в теле. Живая дока просит, чтобы новый файл
    /// был того же типа, что старый: адрес и путь картинки при замене НЕ меняются (замерено:
    /// после замены файла <c>url</c> остался с прежним именем).
    /// </remarks>
    Task<BookStackImage?> ReplaceImageFileAsync(
        int id,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        CancellationToken ct = default);

    /// <summary>
    /// Удаляет картинку (<c>DELETE /api/image-gallery/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Удаление НАСТОЯЩЕЕ, в корзину картинка не уезжает: у корзины свои виды сущностей, картинок
    /// среди них нет. Живая дока прямо предупреждает, что использование картинки не проверяется,
    /// то есть в страницах могут остаться битые ссылки.
    /// </remarks>
    Task DeleteImageAsync(int id, CancellationToken ct = default);

    // ---- Вложения ----

    /// <summary>
    /// Список вложений (<c>GET /api/attachments</c>).
    /// </summary>
    /// <param name="count">Сколько вернуть.</param>
    /// <param name="offset">Сколько пропустить.</param>
    /// <param name="sort">Поле сортировки.</param>
    /// <param name="uploadedTo">Оставить только вложения указанной страницы.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// В списке нет ни <c>content</c>, ни <c>links</c>, а <c>created_by</c> приходит числом.
    /// </remarks>
    Task<BookStackPage<BookStackAttachment>> ListAttachmentsAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        int? uploadedTo = null,
        CancellationToken ct = default);

    /// <summary>
    /// Чтение вложения вместе с содержимым (<c>GET /api/attachments/{id}</c>).
    /// </summary>
    /// <remarks>
    /// Содержимое приходит в поле <see cref="BookStackAttachment.Content"/>, и его смысл зависит
    /// от <see cref="BookStackAttachment.External"/>: у файла это base64, у ссылки сам адрес.
    /// </remarks>
    Task<BookStackAttachment?> GetAttachmentAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Загружает файл вложением (<c>POST /api/attachments</c>, многочастное тело).
    /// </summary>
    /// <param name="uploadedToPageId">Идентификатор страницы. Обязателен.</param>
    /// <param name="name">Видимое имя. Обязательно: именем файла оно не подменяется.</param>
    /// <param name="fileName">Имя файла в многочастном теле.</param>
    /// <param name="content">Байты файла.</param>
    /// <param name="contentType">Тип содержимого.</param>
    /// <param name="ct">Отмена.</param>
    Task<BookStackAttachment?> UploadAttachmentAsync(
        int uploadedToPageId,
        string name,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Создаёт вложение-ссылку (<c>POST /api/attachments</c>, тело JSON).
    /// </summary>
    /// <remarks>
    /// Отдельный метод от загрузки файла, потому что маршрут требует ровно одного из полей
    /// <c>file</c> и <c>link</c> (правило <c>required_without</c>), и объединять их в одном вызове
    /// значило бы разрешить прислать оба.
    /// </remarks>
    Task<BookStackAttachment?> CreateLinkAttachmentAsync(
        BookStackLinkAttachmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Меняет имя, страницу или адрес вложения (<c>PUT /api/attachments/{id}</c>, тело JSON).
    /// </summary>
    /// <remarks>
    /// Замерено: правка поля <c>link</c> у вложения-файла переводит его в ссылку
    /// (<c>external</c> становится <c>true</c>, расширение очищается). То есть этот вызов умеет
    /// менять природу вложения, а не только его подписи.
    /// </remarks>
    Task<BookStackAttachment?> UpdateAttachmentAsync(
        int id, BookStackLinkAttachmentRequest request, CancellationToken ct = default);

    /// <summary>
    /// Заменяет файл вложения, при желании заодно переименовывая его.
    /// </summary>
    /// <remarks>
    /// Уходит <c>POST</c> с полем <c>_method=PUT</c> в теле, проверено вживую (после замены чтение
    /// вернуло base64 нового содержимого).
    /// </remarks>
    Task<BookStackAttachment?> ReplaceAttachmentFileAsync(
        int id,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        CancellationToken ct = default);

    /// <summary>Удаляет вложение (<c>DELETE /api/attachments/{id}</c>). Удаление настоящее, не в корзину.</summary>
    Task DeleteAttachmentAsync(int id, CancellationToken ct = default);

    // ---- Обложки ----

    /// <summary>
    /// Ставит обложку книге.
    /// </summary>
    /// <param name="bookId">Книга.</param>
    /// <param name="fileName">Имя файла в многочастном теле.</param>
    /// <param name="content">Байты картинки.</param>
    /// <param name="contentType">Тип содержимого.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>
    /// Обложка из ответа. Сервер отвечает КНИГОЙ целиком, а не картинкой, и обложка лежит в её
    /// поле <c>cover</c>; здесь возвращается именно она, потому что модель книги живёт в части SDK
    /// про содержимое. Прочитать книгу целиком после этого можно обычным чтением.
    /// </returns>
    /// <remarks>
    /// Уходит <c>POST /api/books/{id}</c> с полями <c>_method=PUT</c> и <c>image</c>. Отдельного
    /// маршрута загрузки обложки у BookStack нет.
    /// <para>
    /// ВАЖНО: у обложки, в отличие от галереи, нечитаемый файл это ОТКАЗ. Замерено 17.08.2026:
    /// тот же самый испорченный PNG, который галерея приняла с кодом 200, тут дал 500 «The server
    /// cannot create thumbnails. Please check you have the GD PHP extension installed». Расширение
    /// при этом на месте, дело именно в файле. Годный PNG 2x2 обложкой встаёт, то есть размер
    /// ни при чём.
    /// </para>
    /// <para>
    /// Снятия обложки тут нет: оно делается обновлением книги полем <c>image: null</c>, то есть
    /// обычным телом JSON, и живёт в части про содержимое.
    /// </para>
    /// </remarks>
    Task<BookStackImage?> SetBookCoverAsync(
        int bookId,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default);

    /// <summary>
    /// Ставит обложку полке. Устроено так же, как у книги, см. <see cref="SetBookCoverAsync"/>:
    /// <c>POST /api/shelves/{id}</c> с полями <c>_method=PUT</c> и <c>image</c>.
    /// </summary>
    Task<BookStackImage?> SetShelfCoverAsync(
        int shelfId,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default);
}
