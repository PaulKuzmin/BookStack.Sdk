namespace BookStackDocsPush;

/// <summary>Каталог хранилища и книга, в которую он едет.</summary>
/// <param name="Key">Путь каталога от корня хранилища, косыми чертами вперёд.</param>
/// <param name="Title">Название книги на портале.</param>
internal sealed record BookMapping(string Key, string Title);

/// <summary>Разобранная командная строка.</summary>
internal sealed record PushArgs(
    string PortalUrl,
    string DocsRoot,
    IReadOnlyList<BookMapping> Books,
    string? Shelf,
    string? ExpectInstance,
    bool Prune,
    bool DryRun,
    bool Verbose,
    TimeSpan Timeout)
{
    public const string Usage = """
        Выкладывает markdown-документы из каталогов на портал BookStack.
        Файлы на диске только читаются и не меняются.

          bookstack-docs-push --to <адрес> --docs <корень> --book <путь>=<Книга> [ещё --book] [ключи]

        Обязательное:
          --to <адрес>         портал, например https://test.help.altway.pro
          --docs <путь>        корень хранилища документов
          --book <путь>=<имя>  каталог от корня хранилища и книга, в которую он едет.
                               Ключ повторяется столько раз, сколько каталогов.

        Ключи:
          --shelf <имя>        сложить книги на полку с таким именем (создаётся при нужде;
                               то, что уже на полке стоит, остаётся)
          --dry-run            прочитать диск и показать раскладку и судьбу ссылок. На портал НЕ
                               ходит вовсе: ни токена, ни сети не требует, --expect-instance при
                               нём не проверяется, потому что проверять не у кого
          --prune              снести на портале страницы, чей файл исчез (по умолчанию только
                               перечисляются). Переименованный файл сюда не попадает: его страница
                               узнаётся по заголовку и главе и переезжает вместе с ним
          --expect-instance <id>  работать, только если instance_id портала совпал с этим
          --timeout <сек>      таймаут одного запроса, по умолчанию 120
          --verbose            показывать запросы к API
          --help               эта справка

        Токен берётся из переменных окружения BOOKSTACK_TOKEN_ID и BOOKSTACK_TOKEN_SECRET,
        а если их нет — из BOOKSTACK_TO_TOKEN_ID и BOOKSTACK_TO_TOKEN_SECRET.
        Токену нужны права заводить и править книги, главы, страницы и полки, а для --prune
        ещё и удалять страницы.

        Как раскладывается дерево. Каталог из --book это книга; подкаталог первого уровня — глава;
        файл — страница. Что лежит глубже, попадает в ту же главу, а путь до себя получает в имя
        страницы. Каталоги и файлы, начинающиеся с точки, пропускаются.

        Пример (PowerShell, перенос строки — обратный апостроф):
          bookstack-docs-push --to https://test.help.altway.pro --docs F:\AltWay\AltWayDocs `
            --book "Архитектура=Архитектура" --book "Будущее=Будущее" `
            --book "Инструкции/Единый-вход=Единый вход" `
            --book "Инструкции/Клиент-WPF=Клиент-WPF" `
            --shelf "Документация проекта" --dry-run
        """;

    /// <exception cref="ArgumentException">Аргументы неполны или непонятны.</exception>
    public static PushArgs? Parse(string[] args)
    {
        string? portal = null, docs = null, shelf = null, instance = null;
        bool prune = false, dry = false, verbose = false;
        var timeout = TimeSpan.FromSeconds(120);
        var books = new List<BookMapping>();

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "/?":
                    return null;
                case "--to":
                    portal = Next(args, ref i);
                    break;
                case "--docs":
                    docs = Next(args, ref i);
                    break;
                case "--book":
                    books.Add(ParseBook(Next(args, ref i)));
                    break;
                case "--shelf":
                    shelf = Next(args, ref i);
                    break;
                case "--expect-instance":
                    instance = Next(args, ref i);
                    break;
                case "--timeout":
                    timeout = ParseSeconds(Next(args, ref i));
                    break;
                case "--prune":
                    prune = true;
                    break;
                case "--dry-run":
                    dry = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Непонятный аргумент: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(portal)) throw new ArgumentException("Не задан --to.");
        if (string.IsNullOrWhiteSpace(docs)) throw new ArgumentException("Не задан --docs.");
        if (books.Count == 0) throw new ArgumentException("Не задан ни один --book.");

        docs = Path.GetFullPath(docs);
        if (!Directory.Exists(docs))
            throw new ArgumentException($"Нет каталога {docs}.");

        var duplicate = books.GroupBy(b => b.Title, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Две книги с именем «{duplicate.Key}»: их содержимое смешалось бы в одной книге.");
        }

        return new PushArgs(
            portal.TrimEnd('/'), docs, books, shelf, instance, prune, dry, verbose, timeout);
    }

    /// <remarks>
    /// Разделитель это первый знак равенства, а не последний: в названии книги он вполне может
    /// встретиться, а в пути от корня хранилища — нет.
    /// </remarks>
    private static BookMapping ParseBook(string value)
    {
        var split = value.IndexOf('=');
        if (split <= 0 || split == value.Length - 1)
            throw new ArgumentException($"Ключ --book пишется как <путь>=<название книги>, а не «{value}».");

        return new BookMapping(
            value[..split].Trim().Replace('\\', '/').Trim('/'),
            value[(split + 1)..].Trim());
    }

    /// <summary>Таймаут в секундах. Отдельным методом ради внятного отказа на чепухе.</summary>
    /// <remarks>
    /// Своя проверка вместо голого разбора: число вне разумных границ (ноль, минус, миллиард)
    /// прошло бы разбор и обернулось либо мгновенным обрывом каждого запроса, либо повисанием
    /// навсегда, а переполнение вылетело бы вовсе без объяснения.
    /// </remarks>
    private static TimeSpan ParseSeconds(string value)
    {
        const int Max = 24 * 60 * 60;

        if (!int.TryParse(value, out var seconds) || seconds < 1 || seconds > Max)
            throw new ArgumentException($"--timeout ждёт целое число секунд от 1 до {Max}, а не «{value}».");

        return TimeSpan.FromSeconds(seconds);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"У аргумента {args[i]} нет значения.");

        return args[++i];
    }
}
