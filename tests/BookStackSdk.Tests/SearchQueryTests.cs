using BookStackSdk.Api;
using BookStackSdk.Search;
using BookStackSdk.Tests.Infrastructure;

namespace BookStackSdk.Tests;

/// <summary>
/// Замок на построитель поискового запроса: чужой текст не должен менять СМЫСЛ запроса.
/// </summary>
/// <remarks>
/// Опасность тут не в отказе, а в его отсутствии. Строку разбирает
/// <c>SearchOptions::addOptionsFromString</c> регулярными выражениями, и кавычка, квадратная или
/// фигурная скобка внутри пользовательского текста превращают одну часть запроса в другую, не давая
/// ни ошибки, ни пометки. Замерено на стенде 17.08.2026: <c>[Скобка=зна]чение]</c> ищет метку со
/// значением <c>зна</c>, а хвост становится отдельным словом; неизвестное имя фильтра молча
/// выбрасывается и запрос отдаёт полную выдачу.
/// <para>
/// Отсюда правило, которое эти тесты и стерегут: экранирование есть ровно у точной фразы (правило
/// взято из <c>ExactSearchOption::toString</c>), а у слов, меток и фильтров его нет, поэтому
/// опасный ввод туда отвергается исключением, а не подчищается тихо.
/// </para>
/// </remarks>
public class SearchQueryTests
{
    [Fact]
    public void Phrase_EscapesQuoteAndBackslash()
    {
        var query = BookStackSearchQuery.Create()
            .AddPhrase("""он сказал "да" про C:\путь""")
            .Build();

        // Обратный слэш удваивается, кавычка прикрывается слэшем: так же, как их снимает
        // обратный разбор SearchOptions::decodeEscapes.
        const string expected = """
            "он сказал \"да\" про C:\\путь"
            """;

        query.Should().Be(expected);
    }

    [Fact]
    public void Phrase_WithBracketsAndBraces_StaysOnePart()
    {
        // Самый опасный ввод: текст, который целиком выглядит как метка и фильтр. Внутри кавычек
        // он остаётся текстом.
        var query = BookStackSearchQuery.Create()
            .AddPhrase("[Отдел=Логистика] {type:page}")
            .Build();

        query.Should().Be("\"[Отдел=Логистика] {type:page}\"");
        query.Should().StartWith("\"").And.EndWith("\"");
    }

    [Fact]
    public void Phrase_Negated_KeepsHyphenOutsideQuotes()
    {
        BookStackSearchQuery.Create().AddPhrase("черновик", negated: true).Build()
            .Should().Be("-\"черновик\"");
    }

    [Theory]
    [InlineData("тек\"ст")]
    [InlineData("тек[ст")]
    [InlineData("тек]ст")]
    [InlineData("тек{ст")]
    [InlineData("тек}ст")]
    [InlineData("два слова")]
    public void Term_RejectsInputThatChangesParsing(string term)
    {
        // У обычных слов экранирования нет вовсе: разбор вырезает из строки все кавычки и скобки,
        // а остаток делит пробелами. Поэтому такой ввод отвергается, а не чистится.
        var act = () => BookStackSearchQuery.Create().AddTerm(term);

        act.Should().Throw<ArgumentException>().WithMessage("*AddPhrase*");
    }

    [Fact]
    public void Term_RejectsLeadingHyphen_BecauseItMeansNegation()
    {
        var act = () => BookStackSearchQuery.Create().AddTerm("-склад");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Term_PlainWord_GoesAsIs()
    {
        BookStackSearchQuery.Create().AddTerm("накладная").Build().Should().Be("накладная");
    }

    [Theory]
    [InlineData("Скоб]ка", "значение")]
    [InlineData("Отдел", "зна]чение")]
    public void Tag_RejectsClosingBracket(string name, string value)
    {
        // Замерено: разбор метки нежадный, \[(.*?)\], и первая же закрывающая скобка обрывает её.
        var act = () => BookStackSearchQuery.Create().AddTag(name, value);

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("unlikely")]
    [InlineData("Цена>")]
    [InlineData("а=б")]
    public void Tag_RejectsOperatorInsideName(string name)
    {
        // TagSearchOption::getParts ищет ПЕРВОЕ вхождение оператора и всё, что до него, считает
        // именем. Имя «unlikely» развалилось бы на «un» и остаток.
        var act = () => BookStackSearchQuery.Create().AddTag(name, "1");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Tag_PutsNoSpacesAroundOperator()
    {
        // Замерено: [Отдел like %Логи%] не находит ничего, потому что ведущий пробел уезжает
        // в значение, а [Отделlike%Логи%] находит.
        BookStackSearchQuery.Create()
            .AddTag("Отдел", "%Логи%", BookStackTagOperator.Like)
            .Build()
            .Should().Be("[Отделlike%Логи%]");
    }

    [Fact]
    public void Tag_WithoutValue_IsPresenceCheck()
    {
        BookStackSearchQuery.Create().AddTag("Отдел").Build().Should().Be("[Отдел]");
        BookStackSearchQuery.Create().AddTag("Отдел", string.Empty).Build().Should().Be("[Отдел]");
    }

    [Theory]
    [InlineData("in_na}me", null)]
    [InlineData("in_name", "зна}чение")]
    [InlineData("in_name:лишнее", null)]
    public void Filter_RejectsInputThatBreaksBraces(string name, string? value)
    {
        var act = () => BookStackSearchQuery.Create().AddFilter(name, value);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Filter_Negated_BuildsWithLeadingHyphen()
    {
        BookStackSearchQuery.Create()
            .AddFilter(BookStackSearchFilter.InName, "склад", negated: true)
            .Build()
            .Should().Be("-{in_name:склад}");
    }

    [Fact]
    public void UpdatedAfter_WritesIsoMomentWithOffset()
    {
        // Смещение пишется всегда: без него сервер истолковал бы момент в своём часовом поясе,
        // а хранит он время в UTC. Непонятую дату разбор молча выбрасывает (замерено).
        BookStackSearchQuery.Create()
            .UpdatedAfter(new DateTimeOffset(2026, 8, 17, 0, 0, 0, TimeSpan.FromHours(10)))
            .Build()
            .Should().Be("{updated_after:2026-08-17T00:00:00+10:00}");
    }

    [Fact]
    public void OfTypes_JoinsWithPipe_AndSkipsEmptyList()
    {
        BookStackSearchQuery.Create().OfTypes("page", "book").Build().Should().Be("{type:page|book}");

        // Пустой список видов не то же самое, что «ни один вид не подходит».
        BookStackSearchQuery.Create().OfTypes().Build().Should().BeEmpty();
    }

    [Fact]
    public void Build_JoinsPartsWithSpaces()
    {
        var query = BookStackSearchQuery.Create()
            .AddTerm("накладная")
            .AddPhrase("отдел \"Логистика\"")
            .AddTag("Статус", "готово")
            .OfTypes("page")
            .Build();

        query.Should().Be("накладная \"отдел \\\"Логистика\\\"\" [Статус=готово] {type:page}");
    }

    [Fact]
    public async Task Search_SendsBuiltQueryEncodedOnce()
    {
        // Транспортная половина того же замка: собранная строка обязана доехать до сервера ровно
        // такой, какой её собрали, и раскодироваться обратно в неё же.
        var stub = StubHttpMessageHandler.Json("""{"data":[],"total":0}""");
        var api = ApiFactory.Create<BookStackSearchApi>(stub);
        var query = BookStackSearchQuery.Create()
            .AddPhrase("отдел \"Логистика\"")
            .AddTag("Статус", "готово");

        await api.SearchAsync(query, page: 2, count: 50);

        var sent = stub.Requests.Single();
        sent.Path.Should().Be("/api/search");

        var sentQuery = ParseQuery(sent.Query);
        sentQuery["query"].Should().Be(query.Build());
        sentQuery["page"].Should().Be("2");
        sentQuery["count"].Should().Be("50");
    }

    [Fact]
    public async Task Search_EmptyQuery_GoesOutEmpty()
    {
        // Пустой запрос НЕ выбрасывается по дороге: сервер должен ответить своим 422
        // «query обязателен» (замерено), а не получить запрос без параметра.
        var stub = StubHttpMessageHandler.Json("""{"data":[],"total":0}""");
        var api = ApiFactory.Create<BookStackSearchApi>(stub);

        await api.SearchAsync(string.Empty);

        stub.Requests.Single().Query.Should().Be("?query=");
    }

    /// <summary>Разбирает строку запроса обратно в пары. Значения раскодируются.</summary>
    private static Dictionary<string, string> ParseQuery(string query)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var at = part.IndexOf('=');
            if (at < 0)
                continue;

            pairs[Uri.UnescapeDataString(part[..at])] = Uri.UnescapeDataString(part[(at + 1)..]);
        }

        return pairs;
    }
}
