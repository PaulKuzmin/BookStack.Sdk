using System.Net;
using System.Net.Http.Headers;
using BookStackSdk.Http;
using BookStackSdk.Internal;
using BookStackSdk.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на повторы: паузу назначает сервер, а решение о самом повторе принимается по статусу и
/// идемпотентности метода, а не по тексту ошибки.
/// </summary>
/// <remarks>
/// Замерено на стенде 17.08.2026 (лимит 180 запросов в минуту, выбран двумя сотнями параллельных
/// вызовов): на <c>429</c> BookStack присылает <c>Retry-After: 31</c> и
/// <c>X-RateLimit-Reset</c> в unix-секундах. Пауза, посчитанная нами вместо назначенной сервером,
/// означала бы новый <c>429</c> и новый круг, поэтому приоритет у заголовков.
/// <para>
/// Время в тестах поддельное (<c>FakeTimeProvider</c>): проверяется именно ДЛИНА паузы, а не факт
/// её наличия, и ждать по-настоящему тридцать одну секунду ради этого не нужно.
/// </para>
/// </remarks>
public class RetryTests
{
    [Fact]
    public async Task Retry_On429_WaitsExactlyRetryAfter()
    {
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            var response = StubHttpMessageHandler.JsonResponse(
                "{}", ++attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);

            if (attempt == 1)
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(31));

            return response;
        });

        var clock = new FakeTimeProvider();
        var started = clock.GetUtcNow();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, clock);

        var send = client.GetAsync("system");

        for (var tick = 0; tick < 60 && !send.IsCompleted; tick++)
        {
            // Сначала двигаем время, потом смотрим, что из этого вышло: проверка ДО первого сдвига
            // ничего не стоит, потому что до него ещё ничего и не могло произойти.
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);

            // Пока назначенная сервером пауза не истекла, второго запроса быть не должно.
            // Расчётная задержка (полсекунды) попадается ровно здесь, на первом же сдвиге.
            if (clock.GetUtcNow() < started + TimeSpan.FromSeconds(31))
                stub.Requests.Count.Should().BeLessThan(2, "сервер назначил паузу в 31 секунду");
        }

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Retry_On429_FallsBackToRateLimitReset()
    {
        // Retry-After приходит не всегда, а X-RateLimit-Reset (момент снятия ограничения
        // в unix-секундах) на 429 приходит вместе с ним. Он и есть запасной вариант.
        var clock = new FakeTimeProvider();
        var started = clock.GetUtcNow();
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            var response = StubHttpMessageHandler.JsonResponse(
                "{}", ++attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK);

            if (attempt == 1)
            {
                response.Headers.TryAddWithoutValidation(
                    "X-RateLimit-Reset", clock.GetUtcNow().AddSeconds(45).ToUnixTimeSeconds().ToString());
                response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", "0");
            }

            return response;
        });

        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, clock);
        var send = client.GetAsync("system");

        for (var tick = 0; tick < 90 && !send.IsCompleted; tick++)
        {
            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(1);

            if (clock.GetUtcNow() < started + TimeSpan.FromSeconds(45))
                stub.Requests.Count.Should().BeLessThan(2, "ограничение снимается только в названный сервером момент");
        }

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Retry_WithoutServerHints_GrowsDelayExponentially()
    {
        // Когда сервер паузу не назвал, она считается сама и УДВАИВАЕТСЯ: полсекунды, секунда,
        // две. Постоянная пауза на общем сбое означала бы, что все клиенты вернутся разом.
        var clock = new FakeTimeProvider();
        var moments = new List<DateTimeOffset>();
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            moments.Add(clock.GetUtcNow());
            return StubHttpMessageHandler.JsonResponse(
                "{}", ++attempt <= 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK);
        });

        var client = BuildClient(
            stub,
            new BookStackOptions { MaxRetryAttempts = 3, RetryBaseDelay = TimeSpan.FromMilliseconds(500) },
            clock);

        var send = client.GetAsync("system");
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromMilliseconds(100));

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        moments.Should().HaveCount(4);

        // Допуск в один шаг прокрутки: продолжение выполняется не мгновенно после срабатывания
        // таймера, и точное совпадение до миллисекунды тут ничего не доказывало бы.
        var tolerance = TimeSpan.FromMilliseconds(150);
        (moments[1] - moments[0]).Should().BeCloseTo(TimeSpan.FromMilliseconds(500), tolerance);
        (moments[2] - moments[1]).Should().BeCloseTo(TimeSpan.FromSeconds(1), tolerance);
        (moments[3] - moments[2]).Should().BeCloseTo(TimeSpan.FromSeconds(2), tolerance);
    }

    [Fact]
    public async Task Retry_On429_AppliesToPostToo()
    {
        // Ограничитель частоты стоит ПЕРЕД обработчиком маршрута: до создания сущности такой
        // запрос не доходит, поэтому повтор безопасен и для POST.
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.JsonResponse(
            "{}", ++attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));

        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 2 }, clock);

        var send = client.PostAsync("books", BookStackJson.CreateContent(new { name = "Книга" }));
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));

        (await send).Dispose();
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Retry_On5xx_DoesNotRepeatPost()
    {
        // Ключа идемпотентности BookStack не даёт, и второй заход после «упало на нашей стороне»
        // оставил бы дубль. Обновление обложки уходит POST-ом с полем _method=PUT, то есть отсюда
        // выглядит как POST и повторено тоже не будет: так и задумано.
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.ServiceUnavailable);
        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, clock);

        var form = BookStackMultipart.ForUpdate().AddFile("image", "cover.png", [1, 2, 3], "image/png");
        var send = client.PostAsync("books/12", form.Build());
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));

        (await send).Dispose();
        stub.Requests.Should().ContainSingle("создание и обновление файлом повторять нельзя");
    }

    [Fact]
    public async Task Retry_On5xx_RepeatsGet()
    {
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.JsonResponse(
            "{}", ++attempt < 3 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK));

        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, clock);

        var send = client.GetAsync("books");
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Retry_StopsAfterMaxAttempts_AndReturnsLastResponse()
    {
        // Исчерпав попытки, обработчик отдаёт последний ответ как есть: превращать его в исключение
        // это дело разбора ответа, а не транспорта.
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.TooManyRequests);
        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 2 }, clock);

        var send = client.GetAsync("books");
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        stub.Requests.Should().HaveCount(3, "первая попытка и два повтора");
    }

    [Fact]
    public async Task Retry_Disabled_SendsOnce()
    {
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.BadGateway);
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 0 }, new FakeTimeProvider());

        (await client.GetAsync("books")).Dispose();

        stub.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_NotAppliedTo4xx()
    {
        // Клиентские ошибки повторять бессмысленно: 422 не станет 200 от второго захода.
        var stub = StubHttpMessageHandler.Json("{}", HttpStatusCode.UnprocessableEntity);
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, new FakeTimeProvider());

        (await client.GetAsync("books")).Dispose();

        stub.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Retry_ResendsBodyBytesAndContentTypeUnchanged()
    {
        // Повтор пересобирает запрос копией. Потеря заголовка содержимого превратила бы вторую
        // попытку в запрос с другим смыслом: у JSON там кодировка, у многочастного тела граница
        // частей, и без любого из них сервер ответит «поле обязательно».
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) => StubHttpMessageHandler.JsonResponse(
            "{}", ++attempt == 1 ? HttpStatusCode.TooManyRequests : HttpStatusCode.OK));

        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 2 }, clock);

        var send = client.PostAsync("books", BookStackJson.CreateContent(new { name = "Инструкция по сборке" }));
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));
        (await send).Dispose();

        stub.Requests.Should().HaveCount(2);
        stub.Requests[1].Body.Should().Equal(stub.Requests[0].Body, "тело повторяется байт в байт");
        stub.Requests[1].ContainsUtf8("Инструкция по сборке").Should().BeTrue();
        stub.Requests[1].ContentType!.CharSet.Should().Be("utf-8");
    }

    [Fact]
    public async Task Retry_OnTransportFailure_RepeatsGet()
    {
        var attempt = 0;
        var stub = new StubHttpMessageHandler((_, _) => ++attempt == 1
            ? throw new HttpRequestException("соединение оборвалось")
            : StubHttpMessageHandler.JsonResponse("{}"));

        var clock = new FakeTimeProvider();
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 2 }, clock);

        var send = client.GetAsync("system");
        await AdvanceUntilCompletedAsync(send, clock, TimeSpan.FromSeconds(1));

        using var response = await send;
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Retry_OnTransportFailure_DoesNotRepeatPost()
    {
        // Оборванное соединение не говорит, дошёл запрос до сервера или нет. Для создания это
        // означает «может быть, сущность уже есть», и повтор тут дороже отказа.
        var stub = new StubHttpMessageHandler((_, _) => throw new HttpRequestException("соединение оборвалось"));
        var client = BuildClient(stub, new BookStackOptions { MaxRetryAttempts = 3 }, new FakeTimeProvider());

        var act = () => client.PostAsync("books", BookStackJson.CreateContent(new { name = "Книга" }));

        await act.Should().ThrowAsync<HttpRequestException>();
        stub.Requests.Should().ContainSingle();
    }

    private static HttpClient BuildClient(
        StubHttpMessageHandler stub, BookStackOptions options, TimeProvider clock)
    {
        options.BaseUrl = ApiFactory.BaseUrl;

        var handler = new BookStackRetryHandler(
            Options.Create(options), NullLogger<BookStackRetryHandler>.Instance, clock)
        {
            InnerHandler = stub,
        };

        return ApiFactory.CreateClient(handler);
    }

    /// <summary>
    /// Прокручивает поддельное время шагами, пока запрос ждёт паузы перед повтором.
    /// </summary>
    /// <remarks>
    /// Шаг задаётся вызывающим: там, где проверяется длина паузы, он должен быть заметно меньше
    /// самой паузы, иначе измерение окажется грубее того, что измеряют.
    /// </remarks>
    private static async Task AdvanceUntilCompletedAsync(Task send, FakeTimeProvider clock, TimeSpan step)
    {
        for (var tick = 0; tick < 1000 && !send.IsCompleted; tick++)
        {
            await Task.Delay(1);
            clock.Advance(step);
        }

        send.IsCompleted.Should().BeTrue("запрос не завершился даже после тысячи шагов времени");
    }
}
