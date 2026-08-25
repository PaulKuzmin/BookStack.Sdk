using System.Net;
using BookStackSdk.Abstractions;
using BookStackSdk.Api;
using BookStackSdk.Models;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Импорт ZIP: загрузка архива и запуск.
/// </summary>
/// <remarks>
/// Здесь проверяется то, чего живая проба не покажет вовремя. Загрузка и запуск это ДВА запроса, и
/// оба ошибаются молча: архив, ушедший не тем полем формы, и запуск без родителя отвергаются уже
/// после того, как файл принят, то есть с висящим импортом на сервере.
/// </remarks>
public class ImportTests
{
    /// <summary>Ответ загрузки: запись импорта, содержимое ещё не создано.</summary>
    private const string PendingImport = """
        {"id":25,"name":"Китайские площадки","path":"uploads/files/imports/7YOpZ6sGIEbYdRFL.zip",
         "size":618462,"type":"book","created_by":1,"created_at":"2026-08-20T18:40:38.000000Z"}
        """;

    /// <summary>Ответ запуска импорта книги: созданная книга.</summary>
    private const string CreatedBook = """
        {"id":77,"name":"Китайские площадки","slug":"kitaiskie-ploshhadki"}
        """;

    private static readonly byte[] ZipBytes = [(byte)'P', (byte)'K', 0x03, 0x04, 1, 2, 3];

    [Fact]
    public async Task Upload_PostsMultipart_WithFileField()
    {
        var stub = StubHttpMessageHandler.Json(PendingImport);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        await api.UploadAsync("book-12.zip", ZipBytes);

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/api/imports");
        sent.ContentType!.MediaType.Should().Be("multipart/form-data");

        var parts = MultipartReader.Read(sent);
        var file = parts.Should().ContainSingle().Subject;
        file.Name.Should().Be("file", "маршрут ждёт поле именно с этим именем, иначе 422 после приёма файла");
        file.FileName.Should().Be("book-12.zip");
        file.Content.Should().Equal(ZipBytes);
    }

    [Fact]
    public async Task Upload_DoesNotSendMethodOverride()
    {
        var stub = StubHttpMessageHandler.Json(PendingImport);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        await api.UploadAsync("book-12.zip", ZipBytes);

        var parts = MultipartReader.Read(stub.Requests.Single());
        parts.Should().NotContain(
            p => p.Name == "_method",
            "загрузка это создание: подмена метода лечит PUT с файлом, а тут честный POST");
    }

    [Fact]
    public async Task Upload_ReadsPendingImport()
    {
        var stub = StubHttpMessageHandler.Json(PendingImport);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        var import = await api.UploadAsync("book-12.zip", ZipBytes);

        import!.Id.Should().Be(25);
        import.Type.Should().Be(BookStackImportType.Book);
        import.Size.Should().Be(618462);
        import.Name.Should().Be("Китайские площадки", "название берётся из архива, а не из имени файла");
    }

    [Fact]
    public async Task RunAsBook_PostsToImportRoute_WithoutParent()
    {
        var stub = StubHttpMessageHandler.Json(CreatedBook);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        var book = await api.RunAsBookAsync(25);

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Path.Should().Be("/api/imports/25");
        sent.BodyAsUtf8.Should().NotContain(
            "parent", "правило маршрута считает пустого родителя заданным, то есть неверным");
        book!.Id.Should().Be(77);
    }

    [Fact]
    public async Task RunAsChapter_SendsParentBook()
    {
        var stub = StubHttpMessageHandler.Json("""{"id":90,"book_id":77,"name":"Глава"}""");
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        var chapter = await api.RunAsChapterAsync(26, bookId: 77);

        var sent = stub.Requests.Single();
        sent.Path.Should().Be("/api/imports/26");
        sent.BodyAsUtf8.Should().Contain("\"parent_type\":\"book\"").And.Contain("\"parent_id\":77");
        chapter!.BookId.Should().Be(77);
    }

    [Fact]
    public async Task RunAsPage_SendsParentChapter()
    {
        var stub = StubHttpMessageHandler.Json("""{"id":101,"book_id":77,"chapter_id":90,"name":"Страница"}""");
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        await api.RunAsPageAsync(27, BookStackImportParent.Chapter, parentId: 90);

        var sent = stub.Requests.Single();
        sent.BodyAsUtf8.Should().Contain("\"parent_type\":\"chapter\"").And.Contain("\"parent_id\":90");
    }

    [Fact]
    public async Task Get_KeepsDetailsRaw()
    {
        // Разбор содержимого архива приходит только при чтении одиночного импорта, и его форма
        // зависит от вида: у книги внутри главы и страницы, у страницы ничего этого нет.
        var stub = StubHttpMessageHandler.Json("""
            {"id":25,"type":"book","details":{"id":4,"name":"Книга","chapters":[],"pages":[{"id":23}]}}
            """);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        var import = await api.GetAsync(25);

        stub.Requests.Single().Path.Should().Be("/api/imports/25");
        import!.Details.Should().NotBeNull();
        import.Details!.Value.GetProperty("pages").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task List_UnwrapsEnvelope()
    {
        var stub = StubHttpMessageHandler.Json($$"""{"data":[{{PendingImport}}],"total":1}""");
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        var page = await api.ListAsync(count: 10, offset: 20);

        stub.Requests.Single().Query.Should().Be("?count=10&offset=20");
        page.Total.Should().Be(1);
        page.Data.Single().Id.Should().Be(25);
    }

    [Fact]
    public async Task Delete_RemovesPendingImport()
    {
        var stub = StubHttpMessageHandler.Empty(HttpStatusCode.NoContent);
        var api = ApiFactory.Create<BookStackImportApi>(stub);

        await api.DeleteAsync(25);

        var sent = stub.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Delete);
        sent.Path.Should().Be("/api/imports/25");
    }
}
