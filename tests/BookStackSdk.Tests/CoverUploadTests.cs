using System.Text;
using System.Text.RegularExpressions;
using BookStackSdk.Api;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на подмену метода у файловых маршрутов: обложки и вложения уходят <c>POST</c>-ом с полем
/// <c>_method=PUT</c> внутри тела, а не настоящим <c>PUT</c>.
/// </summary>
/// <remarks>
/// Причина в PHP, а не в BookStack. Исходник <c>symfony/http-foundation/Request.php</c> в
/// контейнере стенда: многочастное тело при <c>PUT</c> разбирается только на PHP 8.4 и новее,
/// а до этого файлы берутся из <c>$_FILES</c>, который PHP заполняет ТОЛЬКО для <c>POST</c>.
/// То есть на старом PHP файл при настоящем <c>PUT</c> пропадает молча, ответ при этом 200.
/// <para>
/// ВАЖНО: на стенде (BookStack 26.05.3, PHP 8.5.9) настоящий <c>PUT</c> с файлом проходит, обложка
/// встаёт. Это значит, что живая проба стенда поломку НЕ покажет, и поймать возврат к настоящему
/// <c>PUT</c> может только этот тест. На боевой установке help.altway.pro поломка записана как
/// случившаяся.
/// </para>
/// </remarks>
public class CoverUploadTests
{
    private static readonly byte[] PngBytes = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    /// <summary>Ответ сервера на обложку: сущность целиком, обложка внутри неё (замерено).</summary>
    private const string BookWithCover = """
        {"id":12,"name":"Книга","cover":{"id":34,"name":"cover.png","url":"http://bookstack.invalid/uploads/images/cover.png"}}
        """;

    [Fact]
    public async Task SetBookCover_UsesPost_NotPut()
    {
        var stub = StubHttpMessageHandler.Json(BookWithCover);
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.SetBookCoverAsync(12, "cover.png", PngBytes, "image/png");

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Method.Should().NotBe(HttpMethod.Put, "многочастный PUT PHP старше 8.4 не разбирает, и файл пропадает молча");
        sent.Path.Should().Be("/api/books/12");
        sent.ContentType!.MediaType.Should().Be("multipart/form-data");
    }

    [Fact]
    public async Task SetBookCover_CarriesMethodOverrideField()
    {
        var stub = StubHttpMessageHandler.Json(BookWithCover);
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.SetBookCoverAsync(12, "cover.png", PngBytes, "image/png");

        var parts = MultipartReader.Read(stub.Requests.Single());
        var methodField = parts.Should().ContainSingle(p => p.Name == "_method").Subject;
        methodField.Text.Should().Be("PUT", "без этого поля Laravel разберёт запрос как создание, а не обновление");
        parts[0].Name.Should().Be("_method", "поле подмены проверено вживую первым, и порядок держим проверенный");
    }

    [Fact]
    public async Task SetBookCover_CarriesFilePartUnchanged()
    {
        var stub = StubHttpMessageHandler.Json(BookWithCover);
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.SetBookCoverAsync(12, "cover.png", PngBytes, "image/png");

        var file = MultipartReader.Read(stub.Requests.Single()).Should().ContainSingle(p => p.Name == "image").Subject;
        file.FileName.Should().Be("cover.png");
        file.ContentType.Should().Be("image/png");
        file.Content.Should().Equal(PngBytes, "байты файла обязаны доехать без единой правки");
    }

    [Fact]
    public async Task SetBookCover_ReadsCoverOutOfEntityResponse()
    {
        // Сервер отвечает книгой целиком, обложка лежит в её поле cover.
        var stub = StubHttpMessageHandler.Json(BookWithCover);
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        var cover = await api.SetBookCoverAsync(12, "cover.png", PngBytes, "image/png");

        cover.Should().NotBeNull();
        cover!.Id.Should().Be(34);
    }

    [Fact]
    public async Task SetShelfCover_HasSameShapeAsBookCover()
    {
        var stub = StubHttpMessageHandler.Json("""{"id":7,"cover":null}""");
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        var cover = await api.SetShelfCoverAsync(7, "cover.png", PngBytes, "image/png");

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/api/shelves/7");
        MultipartReader.Read(sent).Should().ContainSingle(p => p.Name == "_method" && p.Text == "PUT");

        // Пустой cover при 200 это «обложки нет», а не сбой разбора.
        cover.Should().BeNull();
    }

    [Fact]
    public async Task UploadImage_OnCreate_HasNoMethodOverride()
    {
        // Обратная сторона того же замка: на создании поля подмены быть НЕ должно, иначе Laravel
        // отправит запрос в обновление несуществующей записи.
        var stub = StubHttpMessageHandler.Json("""{"id":5}""");
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.UploadImageAsync(uploadedToPageId: 3, fileName: "shot.png", content: PngBytes, contentType: "image/png");

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);

        var parts = MultipartReader.Read(sent);
        parts.Should().NotContain(p => p.Name == "_method");
        parts.Should().ContainSingle(p => p.Name == "type" && p.Text == "gallery");
        parts.Should().ContainSingle(p => p.Name == "uploaded_to" && p.Text == "3");
        parts.Should().NotContain(p => p.Name == "name", "пустое имя означало бы «назови картинку пустотой», а его отсутствие означает «возьми имя файла»");
    }

    [Fact]
    public async Task ReplaceAttachmentFile_UsesPostWithOverride()
    {
        var stub = StubHttpMessageHandler.Json("""{"id":9}""");
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.ReplaceAttachmentFileAsync(9, "smeta.pdf", [1, 2, 3], "application/pdf");

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/api/attachments/9");

        var parts = MultipartReader.Read(sent);
        parts.Should().ContainSingle(p => p.Name == "_method" && p.Text == "PUT");
        parts.Should().ContainSingle(p => p.Name == "file" && p.FileName == "smeta.pdf");
    }

    [Fact]
    public async Task RenameImage_UsesRealPut_BecauseThereIsNoFile()
    {
        // Подмена метода лечит пропажу ФАЙЛОВ, и больше ничего. Там, где файла нет, ставить её
        // незачем: запрос уходит настоящим PUT с телом JSON.
        var stub = StubHttpMessageHandler.Json("""{"id":5,"name":"Схема"}""");
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.RenameImageAsync(5, "Схема");

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Put);
        sent.ContentType!.MediaType.Should().Be("application/json");
        sent.BodyAsUtf8.Should().NotContain("_method");
    }

    [Fact]
    public async Task FileRoutes_NeverGoOutAsMultipartPut()
    {
        // Сквозная проверка по всем файловым вызовам сразу: настоящего PUT с многочастным телом
        // не должно быть ни одного. Именно такой запрос на боевом PHP теряет файл, отвечая 200.
        var stub = StubHttpMessageHandler.Json(BookWithCover);
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.SetBookCoverAsync(12, "cover.png", PngBytes, "image/png");
        await api.SetShelfCoverAsync(7, "cover.png", PngBytes, "image/png");
        await api.UploadImageAsync(3, "shot.png", PngBytes, "image/png");
        await api.ReplaceImageFileAsync(5, "shot.png", PngBytes, "image/png");
        await api.UploadAttachmentAsync(3, "Смета", "smeta.pdf", [1, 2, 3]);
        await api.ReplaceAttachmentFileAsync(9, "smeta.pdf", [1, 2, 3]);

        stub.Requests.Should().HaveCount(6);
        stub.Requests.Should().AllSatisfy(r =>
        {
            r.Method.Should().Be(HttpMethod.Post);
            r.ContentType!.MediaType.Should().Be("multipart/form-data");
        });
    }
}

/// <summary>
/// Разбор многочастного тела из перехваченного запроса.
/// </summary>
/// <remarks>
/// Разбор идёт по БАЙТАМ, а не по строке: части несут двоичный файл, и раскодирование тела целиком
/// испортило бы его до неузнаваемости. Границу частей берём из параметра <c>boundary</c> заголовка
/// <c>Content-Type</c>, то есть оттуда же, откуда её берёт сервер.
/// </remarks>
internal static class MultipartReader
{
    private static readonly byte[] HeaderSeparator = "\r\n\r\n"u8.ToArray();

    public static IReadOnlyList<MultipartPart> Read(CapturedRequest request)
    {
        var boundary = request.ContentType?.Parameters
            .FirstOrDefault(p => string.Equals(p.Name, "boundary", StringComparison.OrdinalIgnoreCase))?
            .Value?.Trim('"');

        if (string.IsNullOrEmpty(boundary))
            throw new InvalidOperationException("У запроса нет границы частей: тело не многочастное.");

        var delimiter = Encoding.ASCII.GetBytes("--" + boundary);
        var marks = FindAll(request.Body, delimiter);
        var parts = new List<MultipartPart>();

        for (var i = 0; i + 1 < marks.Count; i++)
        {
            var start = marks[i] + delimiter.Length;
            var end = marks[i + 1];

            // Между границами лежит CRLF, заголовки, пустая строка, содержимое и снова CRLF.
            var segment = request.Body[start..end];
            segment = Trim(segment);

            var headerEnd = IndexOf(segment, HeaderSeparator, 0);
            if (headerEnd < 0)
                continue;

            var headers = Encoding.UTF8.GetString(segment[..headerEnd]);
            var content = segment[(headerEnd + HeaderSeparator.Length)..];

            parts.Add(new MultipartPart(
                ReadDispositionValue(headers, "name") ?? string.Empty,
                ReadDispositionValue(headers, "filename"),
                ReadHeader(headers, "Content-Type"),
                content));
        }

        return parts;
    }

    private static string? ReadDispositionValue(string headers, string key)
    {
        // Взгляд назад нужен, чтобы «name» не поймалось внутри «filename»: порядок частей заголовка
        // не оговорён, и полагаться на то, что первым идёт нужный, нельзя.
        var match = Regex.Match(
            headers, @"(?<![\w-])" + key + @"=(?:""([^""]*)""|([^;\r\n]+))", RegexOptions.IgnoreCase);
        if (!match.Success)
            return null;

        return match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value.Trim();
    }

    private static string? ReadHeader(string headers, string name)
    {
        foreach (var line in headers.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
                return line[(name.Length + 1)..].Trim();
        }

        return null;
    }

    /// <summary>
    /// Снимает по ОДНОМУ переводу строки с каждого края.
    /// </summary>
    /// <remarks>
    /// Ровно по одному, а не «все подряд»: они принадлежат разметке многочастного тела, а всё
    /// остальное это содержимое части. Файл, кончающийся байтом перевода строки, при жадной чистке
    /// приехал бы в тест короче, чем ушёл на провод, и тест доказывал бы не то.
    /// </remarks>
    private static byte[] Trim(byte[] segment)
    {
        var start = segment.Length >= 2 && segment[0] == (byte)'\r' && segment[1] == (byte)'\n' ? 2 : 0;
        var end = segment.Length;

        if (end - start >= 2 && segment[end - 2] == (byte)'\r' && segment[end - 1] == (byte)'\n')
            end -= 2;

        return segment[start..end];
    }

    private static List<int> FindAll(byte[] haystack, byte[] needle)
    {
        var found = new List<int>();
        var from = 0;

        while (true)
        {
            var at = IndexOf(haystack, needle, from);
            if (at < 0)
                return found;

            found.Add(at);
            from = at + needle.Length;
        }
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int from)
    {
        for (var start = from; start <= haystack.Length - needle.Length; start++)
        {
            var matched = true;
            for (var i = 0; i < needle.Length && matched; i++)
                matched = haystack[start + i] == needle[i];

            if (matched)
                return start;
        }

        return -1;
    }
}

/// <summary>Одна часть многочастного тела.</summary>
/// <param name="Name">Имя поля формы.</param>
/// <param name="FileName">Имя файла, если часть файловая.</param>
/// <param name="ContentType">Тип содержимого части вместе с кодировкой у текстовых частей.</param>
/// <param name="Content">Содержимое части байтами.</param>
internal sealed record MultipartPart(string Name, string? FileName, string? ContentType, byte[] Content)
{
    /// <summary>Содержимое текстовой части, раскодированное как UTF-8.</summary>
    public string Text => Encoding.UTF8.GetString(Content);
}
