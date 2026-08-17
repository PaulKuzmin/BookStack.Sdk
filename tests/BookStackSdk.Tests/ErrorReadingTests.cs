using System.Net;
using System.Text;
using BookStackSdk.Abstractions;
using BookStackSdk.Api;
using BookStackSdk.Errors;
using BookStackSdk.Models;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на чтение отказов: конверт <c>{"error":{…}}</c> обязан доезжать до исключения целиком,
/// а не-JSON тело не должно подменять ошибку вызова ошибкой разбора.
/// </summary>
/// <remarks>
/// Конверт один на все отказы, это проверено на стенде 17.08.2026 четырьмя разными: 401 без токена,
/// 404 на чужой id, 422 на пустое тело, 429 на выбранный лимит. Тело при этом всё равно может
/// оказаться не-JSON: перед приложением стоит nginx, и его собственные страницы (502, 504, отказ по
/// размеру тела) приходят HTML-ом. Если бы разбор такого тела бросал <c>JsonException</c>,
/// вызывающий получал бы вместо «сервер ответил 502» невнятное «неожиданный символ в позиции 0»,
/// и ловить ему пришлось бы два разных типа исключения вместо одного.
/// </remarks>
public class ErrorReadingTests
{
    [Fact]
    public async Task Error_EnvelopeReachesException()
    {
        const string body = """{"error":{"message":"The requested resource could not be found.","code":404}}""";
        var stub = StubHttpMessageHandler.Json(body, HttpStatusCode.NotFound);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.GetBookAsync(999999);

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.NotFound);
        ex.Error!.Message.Should().Be("The requested resource could not be found.");
        ex.Error.Code.Should().Be(404, "код в теле повторяет HTTP-статус, своей нумерации у BookStack нет");
        ex.RawBody.Should().Be(body, "тело сохраняется как есть и не чистится");
        ex.Message.Should().Contain("404").And.Contain("The requested resource could not be found.");
    }

    [Fact]
    public async Task Validation_FieldNamesReachExceptionAndMessage()
    {
        // Единственная часть ответа, называющая ПОЛЕ: без неё «The given data was invalid» не
        // говорит ничего.
        const string body = """
            {"error":{"message":"The given data was invalid.","validation":{"name":["The name field is required."]},"code":422}}
            """;
        var stub = StubHttpMessageHandler.Json(body, HttpStatusCode.UnprocessableEntity);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.CreateBookAsync(new BookStackBookCreate());

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.ValidationErrors.Should().ContainKey("name");
        ex.ValidationErrors!["name"].Should().ContainSingle().Which.Should().Contain("required");
        ex.Message.Should().Contain("name", "имя поля выносится в текст сообщения, иначе оно бесполезно");
    }

    [Fact]
    public async Task NonJsonBody_DoesNotLeakJsonException()
    {
        // Так выглядит страница nginx: это ответ не приложения, и разбирать его нечем.
        const string html = "<html><head><title>502 Bad Gateway</title></head><body>502 Bad Gateway</body></html>";
        var stub = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent(html, Encoding.UTF8, "text/html"),
        });
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.GetBookAsync(1);

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        ex.Error.Should().BeNull("разобрать нечего, и выдумывать разбор не надо");
        ex.RawBody.Should().Be(html, "сырое тело это единственное, что тут вообще есть");
    }

    [Fact]
    public async Task JsonWithoutErrorKey_LeavesErrorNull()
    {
        // Тело разобралось, но конверта ошибки в нём нет: пустой BookStackError был бы хуже, чем
        // его отсутствие, потому что выглядел бы как разобранный отказ.
        var stub = StubHttpMessageHandler.Json("""{"data":[]}""", HttpStatusCode.BadRequest);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.GetBookAsync(1);

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.Error.Should().BeNull();
        ex.RawBody.Should().Be("""{"data":[]}""");
    }

    [Fact]
    public async Task EmptyBody_FallsBackToReasonPhrase()
    {
        var stub = new StubHttpMessageHandler((_, _) => new HttpResponseMessage(HttpStatusCode.GatewayTimeout)
        {
            ReasonPhrase = "Gateway Timeout",
            Content = new ByteArrayContent([]),
        });
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.GetBookAsync(1);

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.RawBody.Should().BeNull();
        ex.ReasonPhrase.Should().Be("Gateway Timeout");
        ex.Message.Should().Contain("504").And.Contain("Gateway Timeout");
    }

    [Fact]
    public async Task BinaryRoute_ReadsTextEnvelopeToo()
    {
        // Выгрузка возвращает байты, но отказ приходит обычным конвертом, то есть текстом.
        // Читать его надо строкой, иначе сообщение об ошибке превратится в набор байтов.
        var stub = StubHttpMessageHandler.Json(
            """{"error":{"message":"The requested resource could not be found.","code":404}}""",
            HttpStatusCode.NotFound);
        var api = ApiFactory.Create<BookStackExportApi>(stub);

        var act = () => api.ExportBookAsync(999999, BookStackExportFormat.Pdf);

        var ex = (await act.Should().ThrowAsync<BookStackApiException>()).Which;
        ex.Error!.Message.Should().Contain("could not be found");
    }

    [Fact]
    public async Task Delete_EmptyBodyWith204_IsNotAnError()
    {
        // Удаление отвечает 204 без содержимого. Пустое тело это норма, а не сбой разбора.
        var stub = StubHttpMessageHandler.Empty();
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var act = () => api.DeleteBookAsync(3);

        await act.Should().NotThrowAsync();
        stub.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task RecycleBinDestroy_ReadsBodyThatOtherDeletesDoNotHave()
    {
        // Единственное удаление во всём API, отвечающее телом: {"delete_count": N}.
        var stub = StubHttpMessageHandler.Json("""{"delete_count":2}""");
        var api = ApiFactory.Create<BookStackRecycleBinApi>(stub);

        var result = await api.DestroyAsync(41);

        result.Should().NotBeNull();
        stub.Requests.Single().Path.Should().Be("/api/recycle-bin/41");
    }
}
