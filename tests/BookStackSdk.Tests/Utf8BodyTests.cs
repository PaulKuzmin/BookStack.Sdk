using System.Text;
using BookStackSdk.Api;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на кодировку тела: кириллица обязана уходить БАЙТАМИ UTF-8, а не как получится.
/// </summary>
/// <remarks>
/// Это самая дорогая из известных граблей BookStack, потому что она не выглядит как ошибка
/// кодировки. Замерено на стенде 17.08.2026 созданием книги с русским именем пятью способами:
/// байты UTF-8 с явной кодировкой в <c>Content-Type</c> дают 200 и целое имя; байты UTF-8 без
/// параметра <c>charset</c> тоже дают 200; байты cp1251 при заявленном <c>charset=utf-8</c> дают
/// 422 «The name field is required»; байты UTF-8 с меткой порядка байтов (BOM) перед первой скобкой
/// дают тот же 422; тело вовсе без заголовка <c>Content-Type</c> тоже даёт 422. То есть любая порча
/// байтов маскируется под «вы забыли поле», а не под ошибку разбора, и именно на этом питоновская
/// обёртка получала книги с пустым именем.
/// <para>
/// ВАЖНО про способ проверки: тут сравниваются БАЙТЫ, а не строки. Строковая проверка эту порчу
/// пропускает, потому что <c>ReadAsStringAsync</c> раскодирует тело по заявленной кодировке и
/// вернёт целую строку даже тогда, когда на провод ушло не то. Отдельный тест ниже показывает это
/// на живом примере.
/// </para>
/// </remarks>
public class Utf8BodyTests
{
    private const string CyrillicName = "Инструкция по сборке";

    private const string CyrillicDescription = "Описание с ёлкой, «кавычками» и №1";

    [Fact]
    public async Task CreateBook_SendsCyrillicAsUtf8Bytes()
    {
        var stub = StubHttpMessageHandler.Json("""{"id":1,"name":"Инструкция по сборке"}""");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await api.CreateBookAsync(new BookStackBookCreate
        {
            Name = CyrillicName,
            Description = CyrillicDescription,
        });

        var sent = stub.Requests.Single();

        // Байты подряд, а не «строка содержит»: именно это отличает целое тело от испорченного.
        sent.ContainsUtf8(CyrillicName).Should().BeTrue("имя обязано уйти байтами UTF-8");
        sent.ContainsUtf8(CyrillicDescription).Should().BeTrue();

        // И встречная проверка на самую частую подмену: та же строка в UTF-16 в теле появиться
        // не должна ни при каких обстоятельствах.
        sent.ContainsBytes(Encoding.Unicode.GetBytes(CyrillicName)).Should().BeFalse();
    }

    [Fact]
    public async Task CreateBook_DeclaresCharsetInContentType()
    {
        // Сервер параметра charset не требует (замерено: без него тоже 200), но объявлять его
        // дешевле, чем однажды выяснять, что чей-то прокси решил кодировку за нас.
        var stub = StubHttpMessageHandler.Json("{}");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await api.CreateBookAsync(new BookStackBookCreate { Name = CyrillicName });

        var contentType = stub.Requests.Single().ContentType;
        contentType.Should().NotBeNull("тело без Content-Type сервер отвергает как «поле обязательно»");
        contentType!.MediaType.Should().Be("application/json");
        contentType.CharSet.Should().Be("utf-8");
    }

    [Fact]
    public async Task CreateBook_BodyStartsWithBrace_WithoutBom()
    {
        // Метка порядка байтов перед первой скобкой ломает разбор молча: 422 «поле обязательно».
        var stub = StubHttpMessageHandler.Json("{}");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await api.CreateBookAsync(new BookStackBookCreate { Name = CyrillicName });

        var body = stub.Requests.Single().Body;
        body.Should().NotBeEmpty();
        body[0].Should().Be((byte)'{', "перед первой скобкой не должно быть ничего, включая BOM");
        body.Take(3).Should().NotEqual([(byte)0xEF, (byte)0xBB, (byte)0xBF]);
    }

    [Fact]
    public async Task CreateBook_DoesNotEscapeCyrillicIntoAscii()
    {
        // Эскейп-последовательности сервер понимает, тут дело не в нём: тело, в котором русский
        // текст записан как И, невозможно сверить глазами при разборе происшествия.
        var stub = StubHttpMessageHandler.Json("{}");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await api.CreateBookAsync(new BookStackBookCreate { Name = CyrillicName });

        stub.Requests.Single().BodyAsUtf8.Should().NotContain("\\u04");
    }

    [Fact]
    public async Task UploadAttachment_SendsCyrillicFieldAsUtf8Bytes()
    {
        // У многочастного тела кодировка своя у каждой части, и та же грабля работает там же:
        // испорченное имя вложения вернётся как «поле обязательно».
        var stub = StubHttpMessageHandler.Json("""{"id":7}""");
        var api = ApiFactory.Create<BookStackUploadsApi>(stub);

        await api.UploadAttachmentAsync(
            uploadedToPageId: 3,
            name: CyrillicName,
            fileName: "smeta.pdf",
            content: [1, 2, 3]);

        var sent = stub.Requests.Single();
        sent.ContainsUtf8(CyrillicName).Should().BeTrue();
        sent.BodyAsUtf8.Should().Contain("text/plain; charset=utf-8", Exactly.Times(2),
            "кодировка объявляется у каждой текстовой части: имени и uploaded_to");
    }

    [Fact]
    public async Task ByteCheck_CatchesBrokenEncoding_WhereStringCheckDoesNot()
    {
        // Этот тест проверяет не SDK, а способ проверки: он показывает, что байтовое сравнение
        // ловит ровно ту порчу, которую строковое пропускает. Роль cp1251 из питоновской истории
        // тут играет UTF-16: она есть в стандартной поставке и точно так же не та, которую ждёт
        // сервер, а раскодировать её обратно в целую строку клиент умеет.
        var json = $$"""{"name":"{{CyrillicName}}"}""";
        using var broken = new StringContent(json, Encoding.Unicode, "application/json");

        // Строковая проверка проходит: клиент раскодировал тело по заявленной кодировке.
        (await broken.ReadAsStringAsync()).Should().Contain(CyrillicName);

        var stub = StubHttpMessageHandler.Json("{}");
        await ApiFactory.CreateClient(stub).PostAsync("books", broken);

        // Байтовая не проходит: на проводе не UTF-8, и сервер увидел бы «поле обязательно».
        stub.Requests.Single().ContainsUtf8(CyrillicName).Should().BeFalse();

        // А то, что собирает SDK, ту же проверку проходит.
        var stubOk = StubHttpMessageHandler.Json("{}");
        await ApiFactory.CreateClient(stubOk).PostAsync("books", BookStackJson.CreateContent(new { name = CyrillicName }));
        stubOk.Requests.Single().ContainsUtf8(CyrillicName).Should().BeTrue();
    }
}
