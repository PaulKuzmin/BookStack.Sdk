namespace BookStackDocsPush;

/// <summary>
/// Разбор хранилища и рассказ о том, что получится: раскладка и судьба ссылок.
/// </summary>
/// <remarks>
/// Отделено от выкладки нарочно: здесь нет ни одного обращения к порталу, поэтому
/// <c>--dry-run</c> работает без токена и без сети. Проверять раскладку сотни страниц, имея на
/// руках только «сходи попробуй», значит проверять её на портале.
/// </remarks>
internal static class DocsPlan
{
    /// <summary>Читает все каталоги из аргументов.</summary>
    public static Dictionary<string, IReadOnlyList<DocPage>> Scan(PushArgs args)
        => args.Books.ToDictionary(
            book => book.Key,
            book => DocsScanner.Scan(args.DocsRoot, book.Key),
            StringComparer.Ordinal);

    /// <summary>Печатает раскладку по книгам и главам.</summary>
    public static void Print(IReadOnlyDictionary<string, IReadOnlyList<DocPage>> scanned, PushArgs args)
    {
        Console.WriteLine($"Хранилище: {args.DocsRoot}");

        foreach (var (key, pages) in scanned)
        {
            var title = args.Books.First(b => b.Key == key).Title;
            Console.WriteLine($"  {key} → книга «{title}»: страниц {pages.Count}");

            foreach (var chapter in pages.GroupBy(p => p.Chapter).OrderBy(g => g.Key, StringComparer.Ordinal))
                Console.WriteLine($"      {chapter.Key ?? "(в корне книги)"}: {chapter.Count()}");
        }
    }

    /// <summary>
    /// Ловит то, на чём прогон свалился бы или сработал бы не так, ДО первой записи на портал.
    /// </summary>
    /// <remarks>
    /// Проверка стои́т отдельным шагом, потому что обе беды всплывают только посреди выкладки, когда
    /// книги и часть страниц уже созданы: слишком длинное имя портал отвергает отказом, а два файла
    /// с одинаковым заголовком в одной главе на портале различить нечем.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Есть имя, которое портал не примет.</exception>
    public static void Validate(IReadOnlyDictionary<string, IReadOnlyList<DocPage>> scanned, PushArgs args)
    {
        const int NameLimit = 255;

        var badBook = args.Books.FirstOrDefault(b => b.Title.Length > NameLimit || string.IsNullOrWhiteSpace(b.Title));
        if (badBook is not null)
        {
            throw new InvalidOperationException(
                $"Название книги «{badBook.Title}» портал не примет: оно пустое либо длиннее "
                + $"{NameLimit} знаков. Поправьте ключ --book.");
        }

        var badName = scanned.Values.SelectMany(pages => pages)
            .Where(p => p.Name.Length > NameLimit || string.IsNullOrWhiteSpace(p.Name))
            .ToList();

        if (badName.Count > 0)
        {
            throw new InvalidOperationException(
                $"Имя страницы портал принимает непустым и не длиннее {NameLimit} знаков, а отказ "
                + "пришёл бы уже посреди выкладки. Поправьте заголовок в файле:\n"
                + string.Join('\n', badName.Select(p => $"  {p.RelPath} ({p.Name.Length})")));
        }

        // Картинка, вложенная прямо в текст, портал при сохранении выкладывает файлом и подменяет
        // ссылку. В файле на диске остаётся исходная запись, поэтому страница будет считаться
        // изменившейся КАЖДЫЙ прогон, а на портале будет копиться по новой картинке за раз.
        var inlineImages = scanned.Values.SelectMany(pages => pages)
            .Where(p => p.Markdown.Contains("](data:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var page in inlineImages)
        {
            Console.WriteLine(
                $"ВНИМАНИЕ: {page.RelPath} несёт картинку прямо в тексте (data:). Портал выложит её "
                + "файлом и подменит ссылку, поэтому страница будет переписываться каждый прогон.");
        }

        var collisions = scanned.Values.SelectMany(pages => pages)
            .GroupBy(p => (p.BookKey, p.Chapter, p.Name))
            .Where(g => g.Count() > 1);

        foreach (var group in collisions)
        {
            Console.WriteLine(
                $"ВНИМАНИЕ: одинаковый заголовок «{group.Key.Name}» в одной главе — "
                + string.Join(", ", group.Select(p => p.RelPath))
                + ". На портале различить такие страницы будет нечем.");
        }
    }

    /// <summary>
    /// Считает, что станет со ссылками, не создав ни одной страницы.
    /// </summary>
    /// <remarks>
    /// Адреса страниц ещё не существуют, поэтому в карту кладутся заглушки: на этом этапе важно не
    /// «куда поведёт ссылка», а «нашлась ли для неё страница вообще». Числа отсюда совпадают с теми,
    /// что напечатает настоящий прогон.
    /// </remarks>
    public static void PrintLinks(
        IReadOnlyDictionary<string, IReadOnlyList<DocPage>> scanned, PushArgs args)
    {
        var all = scanned.Values.SelectMany(pages => pages).ToList();
        var urls = all.ToDictionary(p => p.RelPath, _ => "…", StringComparer.Ordinal);

        // Каталог, ставший главой, тоже адресуем: ссылки «см. ../Разделы» ведут именно на него.
        foreach (var page in all.Where(p => p.Chapter is not null))
            urls[$"{page.BookKey}/{page.Chapter}"] = "…";

        var rewritten = 0;
        var anchors = 0;
        var unresolved = new List<UnresolvedLink>();

        foreach (var page in all)
        {
            var result = LinkRewriter.Rewrite(page, page.Markdown, urls, rel => FileExists(args, rel));
            rewritten += result.Rewritten;
            anchors += result.Anchors;
            unresolved.AddRange(result.Unresolved);
        }

        Console.WriteLine();
        Console.WriteLine($"Ссылок будет переписано: {rewritten}, останется как есть: {unresolved.Count}.");

        if (anchors > 0)
        {
            Console.WriteLine($"  ссылок на заголовки внутри страниц: {anchors} — на портале они "
                              + "никуда не ведут, BookStack раздаёт заголовкам свои якоря");
        }

        foreach (var group in unresolved.GroupBy(u => u.Reason).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");

            foreach (var link in group.Take(5))
                Console.WriteLine($"    {link.FromPage} → {link.Target}");

            if (group.Count() > 5)
                Console.WriteLine($"    …и ещё {group.Count() - 5}");
        }
    }

    /// <summary>Есть ли такой файл в хранилище. Нужно, чтобы отличить «не переносим» от «нет вовсе».</summary>
    public static bool FileExists(PushArgs args, string relPath)
        => File.Exists(Path.Combine(args.DocsRoot, relPath.Replace('/', Path.DirectorySeparatorChar)));
}
