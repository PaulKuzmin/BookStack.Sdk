namespace BookStackSdk.Models;

/// <summary>
/// Вложение страницы: либо загруженный файл, либо просто ссылка.
/// </summary>
/// <remarks>
/// Две сущности в одной модели это не наше решение, а форма API: маршрут один, а различает их поле
/// <see cref="External"/>. Наборы полей у списка и чтения снова разные (замерено 17.08.2026):
/// список отдаёт <c>created_by</c> числом и не отдаёт <c>links</c> и <c>content</c>, чтение отдаёт
/// <c>created_by</c> объектом плюс оба этих поля.
/// </remarks>
public sealed class BookStackAttachment
{
    /// <summary>Идентификатор.</summary>
    public int? Id { get; set; }

    /// <summary>Видимое имя. Обязательно при создании, именем файла не подменяется.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Расширение загруженного файла без точки. У ссылки пустая строка (замерено), а не
    /// <c>null</c>.
    /// </summary>
    public string? Extension { get; set; }

    /// <summary>Идентификатор страницы, к которой привязано вложение.</summary>
    public int? UploadedTo { get; set; }

    /// <summary>
    /// <c>true</c> означает ссылку, <c>false</c> загруженный файл.
    /// </summary>
    /// <remarks>
    /// Признак меняется правкой: замерено, что <c>PUT</c> с полем <c>link</c> у бывшего файла
    /// переводит <c>external</c> в <c>true</c> и очищает <see cref="Extension"/>. То есть одно
    /// и то же вложение может сменить природу, и запоминать её у себя нельзя.
    /// </remarks>
    public bool? External { get; set; }

    /// <summary>Порядок показа в списке вложений страницы.</summary>
    public int? Order { get; set; }

    /// <summary>Кто загрузил. Число в списке, объект при чтении, см. <see cref="BookStackUserRef"/>.</summary>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто менял последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Когда создано.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда изменено.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Готовая разметка ссылки на вложение. Приходит только при чтении одного вложения.</summary>
    public BookStackAttachmentLinks? Links { get; set; }

    /// <summary>
    /// Содержимое вложения. Приходит только при чтении одного вложения.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: смысл поля зависит от <see cref="External"/>, и это подтверждено замером
    /// 17.08.2026 на паре вложений одной страницы:
    /// <list type="bullet">
    /// <item><c>external: false</c> (файл): тут base64 содержимого файла, например
    /// <c>YmJi</c> для файла со словом <c>bbb</c>. Раскодировать содержимое обязан вызывающий
    /// (<c>Convert.FromBase64String</c>): делать это здесь значило бы решать за него, что делать
    /// с файлом на сто мегабайт;</item>
    /// <item><c>external: true</c> (ссылка): тут сам адрес открытым текстом, например
    /// <c>https://example.com/x</c>.</item>
    /// </list>
    /// Строка «выглядит как base64» ничего не доказывает: короткий адрес тоже может состоять
    /// из подходящих символов. Разбирать надо по <see cref="External"/>, а не по виду строки.
    /// </remarks>
    public string? Content { get; set; }
}

/// <summary>Готовая разметка ссылки на вложение.</summary>
public sealed class BookStackAttachmentLinks
{
    /// <summary>Разметка HTML.</summary>
    public string? Html { get; set; }

    /// <summary>Разметка Markdown.</summary>
    public string? Markdown { get; set; }
}

/// <summary>
/// Тело создания или правки вложения-ССЫЛКИ (<c>POST</c> и <c>PUT /api/attachments</c>).
/// </summary>
/// <remarks>
/// Только для ссылок: у файла тело многочастное, и собирается оно в
/// <see cref="BookStackSdk.Abstractions.IBookStackUploadsApi"/>. Разводить их на два тела пришлось
/// потому, что маршрут требует ровно одного из полей (<c>required_without</c>), а совместить файл
/// и ссылку в одной модели значило бы позволить прислать оба сразу.
/// </remarks>
public sealed class BookStackLinkAttachmentRequest
{
    /// <summary>Видимое имя. Обязательно при создании.</summary>
    public string? Name { get; set; }

    /// <summary>Идентификатор страницы. Обязателен при создании.</summary>
    public int? UploadedTo { get; set; }

    /// <summary>
    /// Адрес. Проверяется правилом <c>safe_url</c>, то есть схемы вроде <c>javascript:</c>
    /// отвергаются сервером.
    /// </summary>
    public string? Link { get; set; }
}
