using System.Globalization;
using System.Text.Json;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackUploadsApi"/>
public sealed class BookStackUploadsApi : BookStackApiBase, IBookStackUploadsApi
{
    /// <summary>Имя поля формы, которого ждут маршруты картинок и обложек.</summary>
    private const string ImageField = "image";

    /// <summary>Имя поля формы, которого ждёт маршрут вложений.</summary>
    private const string FileField = "file";

    public BookStackUploadsApi(HttpClient http, ILogger<BookStackUploadsApi> logger)
        : base(http, logger)
    {
    }

    // ---- Картинки галереи ----

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackImage>> ListImagesAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        int? uploadedTo = null,
        string? type = null,
        CancellationToken ct = default)
    {
        var query = BookStackQuery.Build(
            ("count", count?.ToString(CultureInfo.InvariantCulture)),
            ("offset", offset?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort),
            ("filter[uploaded_to]", uploadedTo?.ToString(CultureInfo.InvariantCulture)),
            ("filter[type]", type));

        var raw = await GetRawAsync("image-gallery" + query, ct).ConfigureAwait(false);
        return UnwrapPage<BookStackImage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackImage?> GetImageAsync(int id, CancellationToken ct = default)
        => Deserialize<BookStackImage>((await GetRawAsync($"image-gallery/{id}", ct).ConfigureAwait(false)).Body);

    /// <inheritdoc />
    public Task<BookStackBinary> GetImageDataAsync(int id, CancellationToken ct = default)
        => GetBinaryAsync($"image-gallery/{id}/data", ct);

    /// <inheritdoc />
    public Task<BookStackBinary> GetImageDataByUrlAsync(string url, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return GetBinaryAsync($"image-gallery/url/data?url={Uri.EscapeDataString(url)}", ct);
    }

    /// <inheritdoc />
    public async Task<BookStackImage?> UploadImageAsync(
        int uploadedToPageId,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        string type = BookStackImageType.Gallery,
        CancellationToken ct = default)
    {
        var form = BookStackMultipart.ForCreate()
            .AddField("type", type)
            .AddField("uploaded_to", uploadedToPageId.ToString(CultureInfo.InvariantCulture));

        // Имя добавляется только если оно есть: пустое поле означало бы «назови картинку пустотой»,
        // а его отсутствие означает «возьми имя файла», и это разные вещи (так в живой доке).
        if (!string.IsNullOrWhiteSpace(name))
            form.AddField("name", name);

        form.AddFile(ImageField, fileName, content, contentType);

        var raw = await SendMultipartAsync("image-gallery", form, ct).ConfigureAwait(false);
        return Deserialize<BookStackImage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackImage?> RenameImageAsync(int id, string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // Тут настоящий PUT с телом JSON: подмена метода лечит только пропажу ФАЙЛОВ,
        // а файла в этом запросе нет.
        var raw = await PutRawAsync($"image-gallery/{id}", new { name }, ct).ConfigureAwait(false);
        return Deserialize<BookStackImage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackImage?> ReplaceImageFileAsync(
        int id,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        CancellationToken ct = default)
    {
        var form = BookStackMultipart.ForUpdate();

        if (!string.IsNullOrWhiteSpace(name))
            form.AddField("name", name);

        form.AddFile(ImageField, fileName, content, contentType);

        var raw = await SendMultipartAsync($"image-gallery/{id}", form, ct).ConfigureAwait(false);
        return Deserialize<BookStackImage>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteImageAsync(int id, CancellationToken ct = default)
        => await DeleteRawAsync($"image-gallery/{id}", ct).ConfigureAwait(false);

    // ---- Вложения ----

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackAttachment>> ListAttachmentsAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        int? uploadedTo = null,
        CancellationToken ct = default)
    {
        var query = BookStackQuery.Build(
            ("count", count?.ToString(CultureInfo.InvariantCulture)),
            ("offset", offset?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort),
            ("filter[uploaded_to]", uploadedTo?.ToString(CultureInfo.InvariantCulture)));

        var raw = await GetRawAsync("attachments" + query, ct).ConfigureAwait(false);
        return UnwrapPage<BookStackAttachment>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackAttachment?> GetAttachmentAsync(int id, CancellationToken ct = default)
        => Deserialize<BookStackAttachment>((await GetRawAsync($"attachments/{id}", ct).ConfigureAwait(false)).Body);

    /// <inheritdoc />
    public async Task<BookStackAttachment?> UploadAttachmentAsync(
        int uploadedToPageId,
        string name,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var form = BookStackMultipart.ForCreate()
            .AddField("name", name)
            .AddField("uploaded_to", uploadedToPageId.ToString(CultureInfo.InvariantCulture))
            .AddFile(FileField, fileName, content, contentType);

        var raw = await SendMultipartAsync("attachments", form, ct).ConfigureAwait(false);
        return Deserialize<BookStackAttachment>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackAttachment?> CreateLinkAttachmentAsync(
        BookStackLinkAttachmentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PostRawAsync("attachments", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackAttachment>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackAttachment?> UpdateAttachmentAsync(
        int id, BookStackLinkAttachmentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PutRawAsync($"attachments/{id}", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackAttachment>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackAttachment?> ReplaceAttachmentFileAsync(
        int id,
        string fileName,
        byte[] content,
        string? contentType = null,
        string? name = null,
        CancellationToken ct = default)
    {
        var form = BookStackMultipart.ForUpdate();

        if (!string.IsNullOrWhiteSpace(name))
            form.AddField("name", name);

        form.AddFile(FileField, fileName, content, contentType);

        var raw = await SendMultipartAsync($"attachments/{id}", form, ct).ConfigureAwait(false);
        return Deserialize<BookStackAttachment>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteAttachmentAsync(int id, CancellationToken ct = default)
        => await DeleteRawAsync($"attachments/{id}", ct).ConfigureAwait(false);

    // ---- Обложки ----

    /// <inheritdoc />
    public Task<BookStackImage?> SetBookCoverAsync(
        int bookId,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default)
        => SetCoverAsync($"books/{bookId}", fileName, content, contentType, ct);

    /// <inheritdoc />
    public Task<BookStackImage?> SetShelfCoverAsync(
        int shelfId,
        string fileName,
        byte[] content,
        string? contentType = null,
        CancellationToken ct = default)
        => SetCoverAsync($"shelves/{shelfId}", fileName, content, contentType, ct);

    /// <summary>
    /// Общая часть обложек книги и полки: маршруты разные, тело одинаковое.
    /// </summary>
    private async Task<BookStackImage?> SetCoverAsync(
        string relativeUrl,
        string fileName,
        byte[] content,
        string? contentType,
        CancellationToken ct)
    {
        var form = BookStackMultipart.ForUpdate()
            .AddFile(ImageField, fileName, content, contentType);

        var raw = await SendMultipartAsync(relativeUrl, form, ct).ConfigureAwait(false);
        return ReadCover(raw.Body);
    }

    /// <summary>
    /// Достаёт обложку из ответа книги или полки.
    /// </summary>
    /// <remarks>
    /// Сервер отвечает СУЩНОСТЬЮ целиком (замерено: приходит книга со всеми полями, а обложка
    /// лежит в её <c>cover</c>). Разбирать её тут нечем: модель книги живёт в части SDK про
    /// содержимое, и тянуть её сюда значило бы связать две независимые части ради одного поля.
    /// Поэтому берётся именно обложка, а прочитать сущность целиком можно обычным чтением.
    /// <para>
    /// Пустой <c>cover</c> при успешном ответе означает, что обложки нет: сервер отдаёт тут
    /// <c>null</c> для сущности без картинки (замерено на книге до загрузки обложки).
    /// </para>
    /// </remarks>
    private static BookStackImage? ReadCover(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return null;

        return root.TryGetProperty("cover", out var cover) && cover.ValueKind == JsonValueKind.Object
            ? cover.Deserialize<BookStackImage>(BookStackJson.Options)
            : null;
    }
}
