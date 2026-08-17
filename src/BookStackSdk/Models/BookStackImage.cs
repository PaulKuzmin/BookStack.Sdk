namespace BookStackSdk.Models;

/// <summary>
/// Картинка BookStack: галерея страниц, рисунки diagrams.net, обложки книг и полок.
/// </summary>
/// <remarks>
/// Набор полей зависит от маршрута, и это снова не вложенные наборы (замерено 17.08.2026 на одной
/// и той же картинке):
/// <list type="bullet">
/// <item>список (<c>GET /api/image-gallery</c>) отдаёт <c>created_by</c> ЧИСЛОМ и не отдаёт
/// <c>thumbs</c> и <c>content</c>;</item>
/// <item>чтение, загрузка и правка отдают <c>created_by</c> ОБЪЕКТОМ плюс <c>thumbs</c>
/// и <c>content</c>;</item>
/// <item>обложка внутри книги или полки (<c>cover</c>) приходит без <c>thumbs</c> и <c>content</c>,
/// зато с <c>type</c> вида <c>cover_book</c> или <c>cover_bookshelf</c>.</item>
/// </list>
/// Отсюда всё nullable, а разбор двух форм <c>created_by</c> живёт в
/// <see cref="BookStackUserRef"/>.
/// </remarks>
public sealed class BookStackImage
{
    /// <summary>Идентификатор.</summary>
    public int? Id { get; set; }

    /// <summary>
    /// Видимое имя. Задаётся полем <c>name</c> запроса, а не именем файла: если <c>name</c>
    /// не передан, сервер подставляет имя файла (так написано в живой доке и подтверждено загрузкой
    /// с русским именем, которое сохранилось целым).
    /// </summary>
    public string? Name { get; set; }

    /// <summary>Полный адрес оригинала.</summary>
    public string? Url { get; set; }

    /// <summary>Путь оригинала относительно корня установки.</summary>
    public string? Path { get; set; }

    /// <summary>
    /// Тип: <c>gallery</c>, <c>drawio</c>, <c>cover_book</c>, <c>cover_bookshelf</c> и прочие
    /// служебные. Загружать через API можно только первые два, см.
    /// <see cref="BookStackImageType"/>.
    /// </summary>
    public string? Type { get; set; }

    /// <summary>
    /// К чему привязана. Для галереи это идентификатор СТРАНИЦЫ, для обложки книги идентификатор
    /// книги, для обложки полки идентификатор полки (замерено).
    /// </summary>
    public int? UploadedTo { get; set; }

    /// <summary>Кто загрузил. Число в списке, объект при чтении, см. <see cref="BookStackUserRef"/>.</summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто менял последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Когда загружена.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда менялась.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Уменьшенные копии. Сам объект приходит только при чтении и загрузке, а его поля могут быть
    /// пустыми, см. <see cref="BookStackImageThumbs"/>.
    /// </summary>
    public BookStackImageThumbs? Thumbs { get; set; }

    /// <summary>Готовая разметка для вставки картинки в страницу.</summary>
    public BookStackImageContent? Content { get; set; }
}

/// <summary>Адреса уменьшенных копий.</summary>
/// <remarks>
/// ВАЖНО: оба поля бывают <c>null</c> при успешном ответе 200, и это НЕ признак того, что копии
/// ещё не готовы. Замерено 17.08.2026 на трёх файлах: испорченный PNG (GD его не читает) грузится
/// в галерею успешно и отдаёт <c>{"gallery": null, "display": null}</c>, а такой же по размеру,
/// но годный PNG 2x2 отдаёт оба адреса. То есть пустые копии означают «сервер не смог прочитать
/// картинку», и вставлять её в страницу нельзя: в разметке <see cref="BookStackImage.Content"/>
/// вместо адреса будет пустая строка (тоже замерено).
/// <para>
/// Тот же самый файл, отправленный ОБЛОЖКОЙ книги, даёт 500 «The server cannot create thumbnails»,
/// то есть у обложек нечитаемый файл это отказ, а у галереи молчаливая половинчатая запись.
/// </para>
/// </remarks>
public sealed class BookStackImageThumbs
{
    /// <summary>Квадрат 150x150 для галереи.</summary>
    public string? Gallery { get; set; }

    /// <summary>Копия шириной до 1680 точек для вставки в страницу.</summary>
    public string? Display { get; set; }
}

/// <summary>
/// Готовая разметка вставки, которую BookStack предлагает для этой картинки. Удобство сервера,
/// а не отдельная сущность: считается из адресов и имени.
/// </summary>
public sealed class BookStackImageContent
{
    /// <summary>Разметка HTML: ссылка на оригинал с картинкой уменьшенной копии внутри.</summary>
    public string? Html { get; set; }

    /// <summary>Разметка Markdown.</summary>
    public string? Markdown { get; set; }
}

/// <summary>
/// Значения поля <c>type</c>, которые принимает загрузка картинки.
/// </summary>
/// <remarks>
/// Список закрыт правилом маршрута <c>in:gallery,drawio</c> из живой доки. Обложки этим маршрутом
/// не загружаются: у них свой путь через обновление книги или полки, см.
/// <see cref="BookStackSdk.Abstractions.IBookStackUploadsApi"/>.
/// </remarks>
public static class BookStackImageType
{
    /// <summary>Обычная картинка в теле страницы.</summary>
    public const string Gallery = "gallery";

    /// <summary>
    /// Рисунок diagrams.net. Живая дока требует, чтобы это был PNG с вшитыми данными схемы:
    /// иначе редактор откроет его как картинку, а не как схему.
    /// </summary>
    public const string Drawio = "drawio";
}
