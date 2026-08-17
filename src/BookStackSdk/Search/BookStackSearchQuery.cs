using System.Globalization;
using System.Text;

namespace BookStackSdk.Search;

/// <summary>
/// Операторы сравнения в фильтре по метке.
/// </summary>
/// <remarks>
/// Список закрыт исходником <c>TagSearchOption::$queryOperators</c>: <c>&lt;=</c>, <c>&gt;=</c>,
/// <c>=</c>, <c>&lt;</c>, <c>&gt;</c>, <c>like</c>, <c>!=</c>. Ничего другого разбор не признает
/// и молча отдаст условие «метка с таким именем есть», см.
/// <see cref="BookStackSearchQuery.AddTag"/>.
/// </remarks>
public enum BookStackTagOperator
{
    /// <summary>Значение равно (<c>=</c>).</summary>
    Equals = 0,

    /// <summary>Значение не равно (<c>!=</c>).</summary>
    NotEquals,

    /// <summary>Значение меньше (<c>&lt;</c>).</summary>
    Less,

    /// <summary>Значение меньше или равно (<c>&lt;=</c>).</summary>
    LessOrEqual,

    /// <summary>Значение больше (<c>&gt;</c>).</summary>
    Greater,

    /// <summary>Значение больше или равно (<c>&gt;=</c>).</summary>
    GreaterOrEqual,

    /// <summary>
    /// Значение по образцу SQL <c>LIKE</c>: подстрока пишется как <c>%кусок%</c>.
    /// </summary>
    Like,
}

/// <summary>
/// Имена фильтров поиска, которые BookStack действительно исполняет.
/// </summary>
/// <remarks>
/// Список снят с исходника <c>SearchRunner</c>: имя фильтра превращается в имя метода
/// (<c>updated_after</c> в <c>filterUpdatedAfter</c>), и если метода нет, фильтр МОЛЧА
/// выбрасывается. Замерено 17.08.2026: <c>{неизвестныйфильтр:1}</c> вернул 200 и полную выдачу,
/// то есть опечатка в имени фильтра не отличима от его отсутствия. Ровно поэтому имена собраны
/// константами, а не пишутся строкой на месте вызова.
/// </remarks>
public static class BookStackSearchFilter
{
    /// <summary>Изменено не раньше указанного момента (<c>updated_at &gt;= дата</c>).</summary>
    public const string UpdatedAfter = "updated_after";

    /// <summary>Изменено раньше указанного момента (<c>updated_at &lt; дата</c>).</summary>
    public const string UpdatedBefore = "updated_before";

    /// <summary>Создано не раньше указанного момента.</summary>
    public const string CreatedAfter = "created_after";

    /// <summary>Создано раньше указанного момента.</summary>
    public const string CreatedBefore = "created_before";

    /// <summary>Создано пользователем. Значение это КОРОТКОЕ ИМЯ (slug) или слово <c>me</c>.</summary>
    public const string CreatedBy = "created_by";

    /// <summary>Изменено пользователем. Значение это короткое имя или <c>me</c>.</summary>
    public const string UpdatedBy = "updated_by";

    /// <summary>Принадлежит пользователю. Значение это короткое имя или <c>me</c>.</summary>
    public const string OwnedBy = "owned_by";

    /// <summary>Подстрока в имени (<c>name LIKE %значение%</c>).</summary>
    public const string InName = "in_name";

    /// <summary>То же самое, что <see cref="InName"/>: в исходнике это синоним.</summary>
    public const string InTitle = "in_title";

    /// <summary>Подстрока в описании или теле (<c>description</c> или <c>text</c>).</summary>
    public const string InBody = "in_body";

    /// <summary>Только с личными правами доступа. Значение не используется.</summary>
    public const string IsRestricted = "is_restricted";

    /// <summary>Просмотренное мной. Значение не используется.</summary>
    public const string ViewedByMe = "viewed_by_me";

    /// <summary>Не просмотренное мной. Значение не используется.</summary>
    public const string NotViewedByMe = "not_viewed_by_me";

    /// <summary>Только шаблоны страниц. Значение не используется.</summary>
    public const string IsTemplate = "is_template";

    /// <summary>
    /// Вид сущности. Несколько видов перечисляются через <c>|</c>, например <c>page|book</c>.
    /// </summary>
    public const string Type = "type";

    /// <summary>
    /// Порядок выдачи.
    /// </summary>
    /// <remarks>
    /// Работает не как сортировка, а как соединение с другой таблицей: единственное значение,
    /// которое понимает исходник, это <c>last_commented</c>, и оно ПРИСОЕДИНЯЕТ комментарии.
    /// Замерено на стенде, где комментариев нет: <c>{sort_by:last_commented}</c> вернул пустую
    /// выдачу при 200. То есть этот фильтр умеет молча выкидывать всё.
    /// </remarks>
    public const string SortBy = "sort_by";
}

/// <summary>
/// Построитель строки запроса к поиску BookStack.
/// </summary>
/// <remarks>
/// Поиск принимает ОДНУ строку в параметре <c>query</c>, и внутри неё живёт свой синтаксис:
/// слова, точные фразы в кавычках, метки в квадратных скобках, фильтры в фигурных, отрицание
/// дефисом впереди. Разбирает эту строку <c>SearchOptions::addOptionsFromString</c> регулярными
/// выражениями, и это главная причина существования построителя: пользовательский текст, попавший
/// в строку как есть, меняет СМЫСЛ запроса, а не ломает его с ошибкой.
/// <para>
/// ВАЖНО про экранирование. Оно есть ровно у одного вида части, у точной фразы, и правило взято
/// не из головы, а из <c>ExactSearchOption::toString</c>: обратный слэш удваивается, кавычка
/// прикрывается слэшем. Обратный разбор (<c>SearchOptions::decodeEscapes</c>) снимает их так же.
/// У обычных слов, меток и фильтров экранирования НЕТ вовсе, поэтому здесь они проверяются
/// и отвергаются исключением: тихо подчистить чужой текст значило бы искать не то, что просили,
/// и не сказать об этом.
/// </para>
/// <para>
/// Отсюда правило применения: любой текст, пришедший от человека, отправляйте
/// <see cref="AddPhrase"/>. <see cref="AddTerm"/> годится для слов, про которые вызывающий знает,
/// что они слова.
/// </para>
/// <para>
/// ВАЖНО про молчаливое усечение. <c>SearchOptions::limitOptions</c> обрезает число частей
/// в запросе: для входа под токеном это 10 слов, 4 точные фразы, 8 меток и 10 фильтров. Лишнее
/// выбрасывается БЕЗ ошибки, то есть запрос из двенадцати фильтров исполнится как запрос из десяти,
/// и по ответу это не видно. Построитель ограничение не повторяет и не режет: он собирает то, что
/// попросили, а знать про потолок надо тому, кто строит запрос машинно.
/// </para>
/// </remarks>
public sealed class BookStackSearchQuery
{
    /// <summary>Значение фильтров по пользователю, означающее «тот, чьим токеном идёт вызов».</summary>
    public const string CurrentUser = "me";

    private readonly List<string> _parts = [];

    /// <summary>Пустой построитель.</summary>
    public static BookStackSearchQuery Create() => new();

    /// <summary>
    /// Добавляет обычное слово.
    /// </summary>
    /// <remarks>
    /// Слова ищутся по индексу и влияют на ранг выдачи, в отличие от точной фразы, которая ищется
    /// подстрокой по тексту. Экранирования у слов нет: разбор просто делит остаток строки
    /// пробелами, предварительно вырезав из неё все кавычки, скобки и фигурные скобки. Поэтому
    /// пробел и любой из символов <c>" [ ] { }</c> внутри слова, а также дефис в начале, тут
    /// запрещены: они означали бы совсем другой запрос. Разбирать чужую строку на слова должен
    /// вызывающий, а произвольный текст надо слать через <see cref="AddPhrase"/>.
    /// <para>
    /// Отдельная тонкость от <c>SearchOptions::parseStandardTermString</c>: слово, содержащее
    /// разделители индекса, сервер САМ превращает в точную фразу. То есть переданное сюда слово
    /// иногда ищется как подстрока, и это не наше решение.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Если слово пустое, начинается с дефиса или содержит пробел либо один из символов
    /// <c>" [ ] { }</c>.
    /// </exception>
    public BookStackSearchQuery AddTerm(string term)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        if (term.StartsWith('-'))
            throw new ArgumentException(
                "Дефис в начале слова означает отрицание. Для отрицания есть точная фраза: AddPhrase(текст, negated: true).",
                nameof(term));

        foreach (var forbidden in TermBreakers)
        {
            if (term.Contains(forbidden))
                throw new ArgumentException(
                    $"Слово содержит символ '{forbidden}', меняющий разбор запроса, а экранирования у слов нет. " +
                    "Отправьте текст точной фразой: AddPhrase(текст).",
                    nameof(term));
        }

        _parts.Add(term);
        return this;
    }

    /// <summary>
    /// Добавляет точную фразу: <c>"текст"</c> или <c>-"текст"</c> при отрицании.
    /// </summary>
    /// <param name="phrase">Текст. Экранируется целиком, чужой ввод безопасен.</param>
    /// <param name="negated">Отрицание: исключить найденное этой фразой.</param>
    /// <remarks>
    /// Единственный вид части, у которого экранирование есть. Правило повторяет
    /// <c>ExactSearchOption::toString</c> символ в символ, и проверено вживую: фраза
    /// с кавычками внутри нашла страницу, где такой текст есть, и не нашла там, где его нет.
    /// </remarks>
    public BookStackSearchQuery AddPhrase(string phrase, bool negated = false)
    {
        ArgumentNullException.ThrowIfNull(phrase);

        var escaped = phrase.Replace("\\", "\\\\").Replace("\"", "\\\"");
        _parts.Add($"{(negated ? "-" : string.Empty)}\"{escaped}\"");
        return this;
    }

    /// <summary>
    /// Добавляет фильтр по метке: <c>[имя]</c>, <c>[имя=значение]</c> или с отрицанием впереди.
    /// </summary>
    /// <param name="name">Имя метки.</param>
    /// <param name="value">
    /// Значение. <c>null</c> или пустая строка означают «метка с таким именем есть», без сравнения
    /// значения (так устроен <c>SearchRunner::applyTagSearch</c>).
    /// </param>
    /// <param name="op">Оператор сравнения значения.</param>
    /// <param name="negated">Отрицание: у сущности такой метки быть не должно.</param>
    /// <remarks>
    /// ВАЖНО, чем это опасно и почему тут проверки, а не тихая склейка.
    /// <list type="bullet">
    /// <item>Закрывающая квадратная скобка обрывает метку: разбор идёт нежадным выражением
    /// <c>\[(.*?)\]</c>. Замерено, что <c>[Скобка=зна]чение]</c> ищет метку со значением
    /// <c>зна</c>, а хвост <c>чение]</c> становится отдельным словом, и находится при этом ничего.
    /// Экранирования у меток нет, поэтому такое значение отвергается исключением.</item>
    /// <item>Имя метки не должно содержать оператор: <c>TagSearchOption::getParts</c> ищет ПЕРВОЕ
    /// вхождение любого из <c>&lt;= &gt;= = &lt; &gt; like !=</c> и всё, что до него, считает
    /// именем. Имя вроде <c>unlikely</c> развалилось бы на <c>un</c> и остаток.</item>
    /// <item>Пробелы вокруг оператора значимы и потому не ставятся. Замерено: <c>[Отдел like
    /// %Логи%]</c> не находит ничего, потому что в значение уезжает ведущий пробел и сравнение
    /// идёт с <c>' %Логи%'</c>, а <c>[Отделlike%Логи%]</c> находит. Обратите внимание, что
    /// пробел ПЕРЕД оператором прощается, но не разбором, а базой: MySQL сравнивает строки
    /// без хвостовых пробелов. Полагаться на это не стоит.</item>
    /// </list>
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Если имя пустое, если имя или значение содержат <c>]</c>, или если имя содержит один
    /// из операторов сравнения.
    /// </exception>
    public BookStackSearchQuery AddTag(
        string name,
        string? value = null,
        BookStackTagOperator op = BookStackTagOperator.Equals,
        bool negated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        EnsureNoClosingBracket(name, nameof(name));
        EnsureNoClosingBracket(value, nameof(value));

        foreach (var token in TagOperatorTokens)
        {
            if (name.Contains(token, StringComparison.Ordinal))
                throw new ArgumentException(
                    $"Имя метки содержит оператор '{token}', и разбор оборвёт имя на нём. " +
                    "Экранирования у меток нет.",
                    nameof(name));
        }

        var body = string.IsNullOrEmpty(value) ? name : name + ToToken(op) + value;
        _parts.Add($"{(negated ? "-" : string.Empty)}[{body}]");
        return this;
    }

    /// <summary>
    /// Добавляет фильтр: <c>{имя}</c>, <c>{имя:значение}</c> или с отрицанием впереди.
    /// </summary>
    /// <param name="name">Имя фильтра. Берите из <see cref="BookStackSearchFilter"/>.</param>
    /// <param name="value">Значение. <c>null</c> для фильтров, которым значение не нужно.</param>
    /// <param name="negated">Отрицание.</param>
    /// <remarks>
    /// Имя, которого нет среди исполняемых, МОЛЧА выбрасывается, см.
    /// <see cref="BookStackSearchFilter"/>. Закрывающая фигурная скобка обрывает фильтр так же,
    /// как квадратная обрывает метку, поэтому она запрещена и здесь.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// Если имя пустое, содержит <c>}</c> или двоеточие, либо значение содержит <c>}</c>.
    /// </exception>
    public BookStackSearchQuery AddFilter(string name, string? value = null, bool negated = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        EnsureNoClosingBrace(name, nameof(name));
        EnsureNoClosingBrace(value, nameof(value));

        if (name.Contains(':'))
            throw new ArgumentException(
                "Двоеточие отделяет имя фильтра от значения, в имени его быть не может.",
                nameof(name));

        var body = string.IsNullOrEmpty(value) ? name : $"{name}:{value}";
        _parts.Add($"{(negated ? "-" : string.Empty)}{{{body}}}");
        return this;
    }

    /// <summary>Изменено не раньше указанного момента.</summary>
    /// <remarks>
    /// Момент уходит в ISO 8601 вместе со смещением часового пояса, например
    /// <c>2026-08-17T00:00:00+10:00</c>. Проверено вживую: такой фильтр отсёк ровно то, что нужно,
    /// а сдвиг на сутки вперёд дал пустую выдачу.
    /// <para>
    /// ВАЖНО: разбирает значение PHP-функция <c>date_create</c>, и НЕПОНЯТАЯ дата не считается
    /// ошибкой. Замерено: <c>{updated_after:вчера}</c> вернул 200 и полную выдачу, то есть условие
    /// просто исчезло. Смещение поэтому пишется всегда: без него сервер истолковал бы момент
    /// в своём часовом поясе, а хранит он время в UTC.
    /// </para>
    /// </remarks>
    public BookStackSearchQuery UpdatedAfter(DateTimeOffset moment, bool negated = false)
        => AddFilter(BookStackSearchFilter.UpdatedAfter, FormatMoment(moment), negated);

    /// <summary>Изменено раньше указанного момента. Про разбор даты см. <see cref="UpdatedAfter"/>.</summary>
    public BookStackSearchQuery UpdatedBefore(DateTimeOffset moment, bool negated = false)
        => AddFilter(BookStackSearchFilter.UpdatedBefore, FormatMoment(moment), negated);

    /// <summary>Создано не раньше указанного момента. Про разбор даты см. <see cref="UpdatedAfter"/>.</summary>
    public BookStackSearchQuery CreatedAfter(DateTimeOffset moment, bool negated = false)
        => AddFilter(BookStackSearchFilter.CreatedAfter, FormatMoment(moment), negated);

    /// <summary>Создано раньше указанного момента. Про разбор даты см. <see cref="UpdatedAfter"/>.</summary>
    public BookStackSearchQuery CreatedBefore(DateTimeOffset moment, bool negated = false)
        => AddFilter(BookStackSearchFilter.CreatedBefore, FormatMoment(moment), negated);

    /// <summary>
    /// Сужает выдачу до перечисленных видов сущностей, например до страниц и книг.
    /// </summary>
    /// <param name="types">
    /// Значения из <see cref="Models.BookStackEntityType"/>. Полка называется <c>bookshelf</c>,
    /// хотя её маршруты зовутся <c>shelves</c>.
    /// </param>
    /// <remarks>
    /// Собирается в <c>{type:page|book}</c>, проверено вживую. Пустой список видов не добавляет
    /// ничего: фильтр без значения означал бы «вид не указан», а это не то же самое, что «ни один
    /// вид не подходит».
    /// </remarks>
    public BookStackSearchQuery OfTypes(params string[] types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var cleaned = types.Where(t => !string.IsNullOrWhiteSpace(t)).ToArray();
        return cleaned.Length == 0
            ? this
            : AddFilter(BookStackSearchFilter.Type, string.Join("|", cleaned));
    }

    /// <summary>
    /// Создано указанным пользователем.
    /// </summary>
    /// <param name="userSlug">
    /// КОРОТКОЕ ИМЯ пользователя (поле <c>slug</c>), а не идентификатор и не почта. Для себя
    /// есть <see cref="CurrentUser"/>.
    /// </param>
    /// <param name="negated">Отрицание.</param>
    /// <remarks>
    /// Так устроен <c>SearchRunner::filterCreatedBy</c>: он ищет пользователя запросом
    /// <c>where('slug','=',значение)</c> и, если не нашёл, УСЛОВИЕ ПРОСТО НЕ ДОБАВЛЯЕТ. Замерено:
    /// <c>{created_by:net-takogo-cheloveka}</c> вернул полную выдачу без единого признака ошибки.
    /// То есть опечатка в имени тут выглядит как «нашлось всё», и проверять существование
    /// пользователя надо заранее. Помнить и о том, что короткое имя МЕНЯЕТСЯ при переименовании
    /// (см. <see cref="Models.BookStackUser.Slug"/>).
    /// </remarks>
    public BookStackSearchQuery CreatedBy(string userSlug, bool negated = false)
        => AddFilter(BookStackSearchFilter.CreatedBy, userSlug, negated);

    /// <summary>Изменено указанным пользователем. Значение это короткое имя, см. <see cref="CreatedBy"/>.</summary>
    public BookStackSearchQuery UpdatedBy(string userSlug, bool negated = false)
        => AddFilter(BookStackSearchFilter.UpdatedBy, userSlug, negated);

    /// <summary>Принадлежит указанному пользователю. Значение это короткое имя, см. <see cref="CreatedBy"/>.</summary>
    public BookStackSearchQuery OwnedBy(string userSlug, bool negated = false)
        => AddFilter(BookStackSearchFilter.OwnedBy, userSlug, negated);

    /// <summary>
    /// Подстрока в имени сущности (<c>{in_name:...}</c>).
    /// </summary>
    /// <remarks>
    /// Ищется как <c>name LIKE %значение%</c>, то есть символы <c>%</c> и <c>_</c> в значении
    /// работают как шаблон SQL. Экранирования у значения фильтра нет, и мы его не подделываем.
    /// </remarks>
    public BookStackSearchQuery InName(string text, bool negated = false)
        => AddFilter(BookStackSearchFilter.InName, text, negated);

    /// <summary>
    /// Подстрока в описании или теле (<c>{in_body:...}</c>). Про шаблон <c>LIKE</c> см.
    /// <see cref="InName"/>.
    /// </summary>
    public BookStackSearchQuery InBody(string text, bool negated = false)
        => AddFilter(BookStackSearchFilter.InBody, text, negated);

    /// <summary>
    /// Собирает строку запроса.
    /// </summary>
    /// <remarks>
    /// Порядок частей на разбор не влияет: <c>SearchOptions</c> сначала выбирает из строки все
    /// кавычки, скобки и фигурные скобки регулярными выражениями, а остаток делит пробелами.
    /// Пустая строка это не «искать всё»: маршрут объявляет <c>query</c> обязательным, и пустое
    /// значение даёт 422 (замерено).
    /// </remarks>
    public string Build() => string.Join(" ", _parts);

    /// <inheritdoc />
    public override string ToString() => Build();

    /// <summary>Символы, ломающие разбор обычного слова.</summary>
    private static readonly char[] TermBreakers = ['"', '[', ']', '{', '}', ' ', '\t', '\r', '\n'];

    /// <summary>
    /// Операторы метки в том же виде, в каком их ищет разбор. Перечислены как в исходнике, включая
    /// строчный <c>like</c>: сравнение там регистрозависимое.
    /// </summary>
    private static readonly string[] TagOperatorTokens = ["<=", ">=", "=", "<", ">", "like", "!="];

    private static string ToToken(BookStackTagOperator op) => op switch
    {
        BookStackTagOperator.Equals => "=",
        BookStackTagOperator.NotEquals => "!=",
        BookStackTagOperator.Less => "<",
        BookStackTagOperator.LessOrEqual => "<=",
        BookStackTagOperator.Greater => ">",
        BookStackTagOperator.GreaterOrEqual => ">=",
        BookStackTagOperator.Like => "like",
        _ => throw new ArgumentOutOfRangeException(nameof(op), op, "Такого оператора у меток нет."),
    };

    /// <summary>Момент в ISO 8601 со смещением: <c>2026-08-17T00:00:00+10:00</c>.</summary>
    private static string FormatMoment(DateTimeOffset moment)
        => moment.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);

    private static void EnsureNoClosingBracket(string? value, string parameterName)
    {
        if (value is not null && value.Contains(']'))
            throw new ArgumentException(
                "Закрывающая квадратная скобка обрывает фильтр по метке, а экранирования у меток нет.",
                parameterName);
    }

    private static void EnsureNoClosingBrace(string? value, string parameterName)
    {
        if (value is not null && value.Contains('}'))
            throw new ArgumentException(
                "Закрывающая фигурная скобка обрывает фильтр, а экранирования у фильтров нет.",
                parameterName);
    }
}
