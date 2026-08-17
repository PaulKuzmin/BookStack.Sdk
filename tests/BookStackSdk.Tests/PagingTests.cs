using BookStackSdk.Api;
using BookStackSdk.Models;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на листание: окно задаётся смещением, а не курсором, и остановка обхода держится на трёх
/// признаках сразу.
/// </summary>
/// <remarks>
/// Постраничность BookStack устроена как <c>skip()</c> и <c>take()</c> в
/// <c>ListingResponseBuilder::countAndOffsetQuery()</c>, курсора нет. В ответе приходит только
/// <c>total</c>, то есть полное число доступных записей; ни смещение, ни размер окна сервер не
/// повторяет. Поэтому обход останавливается по пустому окну, по окну короче запрошенного и по
/// достигнутому <c>total</c>, и ни один из трёх признаков не может быть единственным: <c>total</c>
/// сервер может не прислать вовсе, а неполное окно бывает только в конце.
/// <para>
/// Отдельная ловушка, ради которой тут проверяется размер окна: <c>count</c> сервер молча зажимает
/// к отрезку от 1 до 500 (замерено: <c>count=0</c> отдал ОДНУ запись). То есть неположительное окно
/// не ломает обход, а превращает его в перебор по одной записи с запросом на каждую.
/// </para>
/// </remarks>
public class PagingTests
{
    [Fact]
    public async Task Enumerate_WalksEveryWindow_AndStopsOnShortOne()
    {
        var stub = Dataset(size: 5);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2 }))
            books.Add(book);

        books.Select(b => b.Id).Should().Equal(1, 2, 3, 4, 5);
        stub.Requests.Should().HaveCount(3, "две полные страницы и одна короткая");
        stub.Requests.Select(r => Offset(r)).Should().Equal(0, 2, 4);
    }

    [Fact]
    public async Task Enumerate_EmptyList_AsksOnceAndYieldsNothing()
    {
        var stub = Dataset(size: 0);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync())
            books.Add(book);

        books.Should().BeEmpty();
        stub.Requests.Should().ContainSingle("пустое окно это конец обхода, а не повод спросить ещё раз");
    }

    [Fact]
    public async Task Enumerate_WhenTotalIsReached_DoesNotAskForOneMorePage()
    {
        // Последняя страница ПОЛНАЯ: признака «окно короче запрошенного» тут нет, и обход
        // обязан остановиться по total, не тратя лишний запрос на заведомо пустое окно.
        var stub = Dataset(size: 4);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2 }))
            books.Add(book);

        books.Should().HaveCount(4);
        stub.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Enumerate_WithoutTotal_StopsOnShortWindow()
    {
        // Сервер поля total не прислал: признака «дошли до полного числа» нет вовсе, и остановить
        // обход может только неполное окно. Лишний запрос за заведомо пустой страницей это не
        // косметика: на каждом обходе он тратит попытку из того самого лимита в 180 в минуту.
        var stub = Dataset(size: 5, sendTotal: false);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2 }))
            books.Add(book);

        books.Should().HaveCount(5);
        stub.Requests.Should().HaveCount(3, "третье окно короче запрошенного, дальше спрашивать нечего");
    }

    [Fact]
    public async Task Enumerate_WithoutTotal_OnExactMultiple_StopsOnEmptyWindow()
    {
        // Записей ровно на целое число окон: неполного окна не будет никогда, и остановка держится
        // на пустом ответе.
        var stub = Dataset(size: 4, sendTotal: false);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2 }))
            books.Add(book);

        books.Should().HaveCount(4);
        stub.Requests.Should().HaveCount(3, "третий запрос вернул пустое окно, на нём обход и кончился");
    }

    [Fact]
    public async Task Enumerate_ServerIgnoringOffset_IsStillBoundedByTotal()
    {
        // Порченый сервер: отдаёт одно и то же полное окно на любое смещение. Признак «окно короче»
        // тут не сработает никогда, и обход держится только на total. Заглушка бросает исключение
        // на одиннадцатом запросе, поэтому зацикливание тут не повиснет, а упадёт с текстом.
        var calls = 0;
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            if (++calls > 10)
                throw new InvalidOperationException("Обход не остановился: сделано больше десяти запросов.");

            return StubHttpMessageHandler.JsonResponse("""{"data":[{"id":1},{"id":2}],"total":6}""");
        });
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2 }))
            books.Add(book);

        books.Should().HaveCount(6, "смещение растёт на длину выданного окна, а не на длину нового");
        stub.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task Enumerate_WithoutCount_AsksForHundred()
    {
        // Сто это умолчание самого BookStack (API_DEFAULT_ITEM_COUNT), и обход без параметров
        // обязан вести себя как обычный список без параметров.
        var stub = Dataset(size: 1);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await foreach (var _ in api.EnumerateBooksAsync())
        {
        }

        stub.Requests.Single().Query.Should().Contain("count=100");
    }

    [Fact]
    public async Task Enumerate_WithNonPositiveCount_FallsBackToDefaultWindow()
    {
        // Ноль пропускать на сервер нельзя: он зажмёт его к единице, обход не сломается, но
        // превратится в перебор по одной записи с запросом на каждую.
        var stub = Dataset(size: 3);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        await foreach (var _ in api.EnumerateBooksAsync(new BookStackListQuery { Count = 0 }))
        {
        }

        stub.Requests.Should().ContainSingle();
        stub.Requests.Single().Query.Should().Contain("count=100").And.NotContain("count=0");
    }

    [Fact]
    public async Task Enumerate_StartsFromRequestedOffset()
    {
        var stub = Dataset(size: 5);
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var books = new List<BookStackBook>();
        await foreach (var book in api.EnumerateBooksAsync(new BookStackListQuery { Count = 2, Offset = 3 }))
            books.Add(book);

        books.Select(b => b.Id).Should().Equal(4, 5);
        stub.Requests.Should().ContainSingle();
        Offset(stub.Requests.Single()).Should().Be(3);
    }

    [Fact]
    public async Task Enumerate_KeepsSortAndFilters_OnEveryWindow()
    {
        // Обход двигает только смещение. Потеря сортировки на второй странице означала бы другой
        // порядок выборки, то есть пропущенные и повторённые записи.
        var stub = Dataset(size: 5);
        var api = ApiFactory.Create<BookStackContentApi>(stub);
        var query = new BookStackListQuery
        {
            Count = 2,
            SortBy = BookStackSortFields.Books.UpdatedAt,
            Descending = false,
        };
        query.Filters["name:like"] = "%склад%";

        await foreach (var _ in api.EnumerateBooksAsync(query))
        {
        }

        stub.Requests.Should().HaveCount(3);
        stub.Requests.Should().AllSatisfy(r =>
        {
            r.Query.Should().Contain("sort=updated_at");
            r.Query.Should().Contain(Uri.EscapeDataString("filter[name:like]"));
        });
    }

    [Fact]
    public async Task List_ReportsRequestedWindowAlongsideServerTotal()
    {
        // Сервер не повторяет ни смещение, ни размер окна, поэтому их кладёт клиент: без них
        // расхождение длины выдачи с ожиданием не с чем сравнивать.
        var stub = StubHttpMessageHandler.Json("""{"data":[{"id":9}],"total":37}""");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var result = await api.ListBooksAsync(new BookStackListQuery { Count = 2, Offset = 4 });

        result.Data.Should().ContainSingle();
        result.Total.Should().Be(37);
        result.RequestedOffset.Should().Be(4);
        result.RequestedCount.Should().Be(2);
    }

    [Fact]
    public async Task List_WithoutTotal_LeavesItNull_NotZero()
    {
        // Ноль это «записей нет», отсутствие поля это «сервер его не прислал». На этом различии
        // строится решение «долистывать ли дальше».
        var stub = StubHttpMessageHandler.Json("""{"data":[{"id":1}]}""");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var result = await api.ListBooksAsync();

        result.Total.Should().BeNull();
        result.Data.Should().ContainSingle();
        result.RequestedCount.Should().BeNull("окно не задавали, сервер взял своё умолчание");
    }

    [Fact]
    public async Task List_EmptyList_IsNotAnError()
    {
        var stub = StubHttpMessageHandler.Json("""{"data":[],"total":0}""");
        var api = ApiFactory.Create<BookStackContentApi>(stub);

        var result = await api.ListBooksAsync();

        result.Data.Should().BeEmpty();
        result.Total.Should().Be(0);
    }

    /// <summary>
    /// Заглушка, которая ведёт себя как списочный маршрут: отдаёт окно из <paramref name="size"/>
    /// записей по смещению и размеру окна из строки запроса.
    /// </summary>
    private static StubHttpMessageHandler Dataset(int size, bool sendTotal = true, int callLimit = 10)
    {
        var calls = 0;

        return new StubHttpMessageHandler((request, _) =>
        {
            if (++calls > callLimit)
                throw new InvalidOperationException($"Обход не остановился: сделано больше {callLimit} запросов.");

            var query = ParseQuery(request.RequestUri!.Query);
            var offset = query.TryGetValue("offset", out var rawOffset) ? int.Parse(rawOffset) : 0;
            var count = query.TryGetValue("count", out var rawCount) ? int.Parse(rawCount) : 100;

            var window = Enumerable
                .Range(offset, Math.Max(0, Math.Min(count, size - offset)))
                .Select(i => $$"""{"id":{{i + 1}},"name":"Книга {{i + 1}}"}""");

            var data = "[" + string.Join(",", window) + "]";
            var body = sendTotal ? $$"""{"data":{{data}},"total":{{size}}}""" : $$"""{"data":{{data}}}""";

            return StubHttpMessageHandler.JsonResponse(body);
        });
    }

    private static int Offset(CapturedRequest request)
        => ParseQuery(request.Query).TryGetValue("offset", out var value) ? int.Parse(value) : 0;

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf('=');
            if (at > 0)
                pairs[Uri.UnescapeDataString(part[..at])] = Uri.UnescapeDataString(part[(at + 1)..]);
        }

        return pairs;
    }
}
