using BookStackSdk.Models;
using BookStackTools;

namespace BookStackDocsPush;

/// <summary>
/// Выкладывает дерево markdown-файлов на портал: книги, главы, страницы, затем ссылки.
/// </summary>
/// <remarks>
/// Порядок работы задан не удобством, а тем, чего нельзя узнать заранее. Адрес страницы на портале
/// известен только ПОСЛЕ её создания (короткое имя раздаёт сервер), а ссылки внутри документов
/// указывают друг на друга. Поэтому проходов два: первый доводит до места сами страницы, второй —
/// их содержимое с уже переписанными ссылками. Обратный порядок означал бы либо угадывание чужих
/// адресов, либо оставленные битыми переходы.
/// <para>
/// Повторный прогон ничего не создаёт заново: страница узнаётся по тегу <c>docs-path</c> с путём
/// файла, и это переживает переименование заголовка. Одинаковый текст не переписывается, чтобы не
/// плодить ревизии на пустом месте.
/// </para>
/// <para>
/// ФАЙЛЫ НА ДИСКЕ НЕ МЕНЯЮТСЯ. Хранилище документов только читается, вся правка идёт на портал.
/// </para>
/// </remarks>
internal sealed class DocsPush(BookStackEndpoint portal, PushArgs args)
{
    /// <summary>Имя тега, которым страница привязана к файлу.</summary>
    private const string PathTag = "docs-path";

    /// <summary>Окно списков. Книг и глав немного, страниц в книге до пары сотен.</summary>
    private const int Window = 500;

    private readonly Dictionary<string, string> _urls = new(StringComparer.Ordinal);
    private readonly List<UnresolvedLink> _unresolved = [];
    private int _created, _renamed, _updated, _unchanged, _adopted, _reordered, _anchors;

    public async Task RunAsync(CancellationToken ct)
    {
        var scanned = DocsPlan.Scan(args);
        DocsPlan.Print(scanned, args);
        DocsPlan.Validate(scanned, args);

        var bookIds = new Dictionary<string, int>(StringComparer.Ordinal);
        var placed = new List<(DocPage Page, RemotePage Remote)>();
        var orphans = new List<RemotePage>();

        // Проход первый: книги, главы и сами страницы на своих местах.
        foreach (var (key, pages) in scanned)
        {
            ct.ThrowIfCancellationRequested();

            var title = args.Books.First(b => b.Key == key).Title;
            Console.WriteLine();
            Console.WriteLine($"Книга «{title}»…");

            var book = await EnsureBookAsync(title, ct);
            bookIds[key] = book.Id!.Value;

            // Книге могли включить правило сортировки: тогда порядок задаёт портал, пересчитывая
            // его после каждой записи. Слать своё в такую книгу значит спорить с сервером на каждом
            // прогоне и переписывать её целиком заново.
            var ordered = book.SortRuleId is null;
            if (!ordered)
                Console.WriteLine("  у книги включено правило сортировки — порядок задаёт портал, не мы");

            // Пустой файл портал страницей не примет, а отказ пришёл бы посреди выкладки. Такие
            // файлы пропускаются, и их страницы (если были) НЕ считаются осиротевшими: опустевший
            // файл не повод сносить написанное.
            var blank = pages.Where(p => string.IsNullOrWhiteSpace(p.Markdown)).ToList();
            var live = pages.Where(p => !string.IsNullOrWhiteSpace(p.Markdown)).ToList();

            foreach (var empty in blank)
                Console.WriteLine($"  пропущен пустой файл: {empty.RelPath}");

            var chapters = await EnsureChaptersAsync(book.Id.Value, live, ordered, ct);
            var remote = await LoadRemoteAsync(book.Id.Value, ct);

            // Ссылки бывают и на каталог целиком («см. ../Разделы»): каталог стал главой, и вести
            // такую ссылку есть куда.
            foreach (var (name, chapter) in chapters)
                _urls[$"{key}/{name}"] = $"{portal.BaseUrl}/books/{book.Slug}/chapter/{chapter.Slug}";

            // Страницы, чей файл в этом прогоне не встретился. Считаются ДО расстановки: из них
            // берутся кандидаты на усыновление (файл переехал или переименовался), а что не
            // разобрали — сироты. Пустые файлы тут учитываются наравне с живыми, иначе их страницы
            // попали бы под --prune.
            var stale = remote.Values
                .Where(r => r.DocsPath is not null && pages.All(p => p.RelPath != r.DocsPath))
                .ToList();

            var claimed = new HashSet<int>();

            foreach (var page in live)
            {
                ct.ThrowIfCancellationRequested();

                var chapterId = page.Chapter is null ? (int?)null : chapters[page.Chapter].Id;
                var settled = await PlaceAsync(book, page, chapterId, ordered, remote, stale, claimed, ct);

                placed.Add((page, settled));
                _urls[page.RelPath] = $"{portal.BaseUrl}/books/{book.Slug}/page/{settled.Slug}";
            }

            orphans.AddRange(stale.Where(r => !claimed.Contains(r.Id)));
        }

        // Проход второй: содержимое со ссылками, которые теперь есть куда направить.
        Console.WriteLine();
        Console.WriteLine("Ссылки и содержимое…");

        foreach (var (page, remote) in placed)
        {
            ct.ThrowIfCancellationRequested();
            await WriteContentAsync(page, remote, ct);
        }

        await EnsureShelfAsync(bookIds.Values.ToList(), ct);
        await HandleOrphansAsync(orphans, ct);

        Report();
    }

    // ---- Книга, главы, снимок портала ----

    /// <remarks>
    /// Найденная книга дочитывается целиком: в списке нет правила сортировки, а от него зависит,
    /// имеет ли смысл вообще задавать порядок страниц.
    /// </remarks>
    private async Task<BookStackBook> EnsureBookAsync(string title, CancellationToken ct)
    {
        var query = new BookStackListQuery { Count = Window };
        query.Filters[BookStackSortFields.Books.Name] = title;

        var found = (await portal.Content.ListBooksAsync(query, ct)).Data
            .FirstOrDefault(b => string.Equals(b.Name, title, StringComparison.Ordinal));

        if (found?.Id is not null)
            return await portal.Content.GetBookAsync(found.Id.Value, ct) ?? found;

        var created = await portal.Content.CreateBookAsync(new BookStackBookCreate { Name = title }, ct)
            ?? throw new InvalidOperationException($"Книга «{title}» не создалась.");

        Console.WriteLine($"  книга создана: #{created.Id} [{created.Slug}]");
        return created;
    }

    /// <summary>Глава портала: то, что нужно и для укладки страниц, и для ссылок на неё.</summary>
    private sealed record RemoteChapter(int Id, string Slug, int Priority);

    /// <summary>
    /// Главы книги по именам, недостающие создаются.
    /// </summary>
    /// <remarks>
    /// Порядок главы задаётся явно, и это не мелочь: на верхнем уровне книги главы и одиночные
    /// страницы выстраиваются ОДНИМ И ТЕМ ЖЕ полем <c>priority</c>. Не пришли своего — сервер
    /// поставит главе своё, взятое от числа уже существующих, и оглавление разойдётся с порядком
    /// файлов на первом же прогоне. Глава встаёт на место своего первого файла.
    /// </remarks>
    private async Task<Dictionary<string, RemoteChapter>> EnsureChaptersAsync(
        int bookId, IReadOnlyList<DocPage> pages, bool ordered, CancellationToken ct)
    {
        var query = new BookStackListQuery { Count = Window };
        query.Filters[BookStackSortFields.Chapters.BookId] = bookId.ToString();

        var existing = (await portal.Content.ListChaptersAsync(query, ct)).Data
            .Where(c => c.Id is not null && c.Name is not null)
            .ToDictionary(c => c.Name!, c => c, StringComparer.Ordinal);

        var map = new Dictionary<string, RemoteChapter>(StringComparer.Ordinal);

        var wanted = pages
            .Where(p => p.Chapter is not null)
            .GroupBy(p => p.Chapter!, StringComparer.Ordinal)
            .Select(g => (Name: g.Key, Priority: g.Min(p => p.Priority)));

        foreach (var (name, priority) in wanted)
        {
            if (existing.TryGetValue(name, out var found))
            {
                if (ordered && found.Priority != priority)
                {
                    await portal.Content.UpdateChapterAsync(
                        found.Id!.Value,
                        new BookStackChapterCreate { Name = name, Priority = priority },
                        ct);
                }

                map[name] = new RemoteChapter(found.Id!.Value, found.Slug ?? string.Empty, priority);
                continue;
            }

            var created = await portal.Content.CreateChapterAsync(
                new BookStackChapterCreate
                {
                    BookId = bookId,
                    Name = name,
                    Priority = ordered ? priority : null,
                }, ct)
                ?? throw new InvalidOperationException($"Глава «{name}» не создалась.");

            Console.WriteLine($"  глава создана: «{name}» #{created.Id}");
            map[name] = new RemoteChapter(created.Id!.Value, created.Slug ?? string.Empty, priority);
        }

        return map;
    }

    /// <summary>Страница портала со всем, что нужно для сравнения.</summary>
    /// <remarks>
    /// Теги хранятся ЦЕЛИКОМ, а не одним нашим. BookStack при записи тегов замещает набор целиком,
    /// поэтому отправить только свой значило бы стереть всё, что на страницу повесил человек, —
    /// молча и без возможности восстановить: ревизии страницы теги не хранят.
    /// </remarks>
    private sealed record RemotePage(
        int Id, string Name, string Slug, int? ChapterId, int Priority, string Markdown,
        string? DocsPath, IReadOnlyList<BookStackTag> Tags);

    /// <summary>
    /// Снимок страниц книги.
    /// </summary>
    /// <remarks>
    /// Каждая страница дочитывается поодиночке, и иначе никак: в списке нет ни содержимого, ни
    /// тегов, а нужно и то и другое — по тегу страница узнаётся, по содержимому решается, надо ли
    /// её вообще трогать.
    /// </remarks>
    private async Task<Dictionary<int, RemotePage>> LoadRemoteAsync(int bookId, CancellationToken ct)
    {
        var query = new BookStackListQuery { Count = Window };
        query.Filters[BookStackSortFields.Pages.BookId] = bookId.ToString();

        var snapshot = new Dictionary<int, RemotePage>();

        await foreach (var brief in portal.Content.EnumeratePagesAsync(query, ct))
        {
            var page = await portal.Content.GetPageAsync(brief.Id!.Value, ct);
            if (page?.Id is null)
                continue;

            var tags = page.Tags ?? [];

            snapshot[page.Id.Value] = new RemotePage(
                page.Id.Value,
                page.Name ?? string.Empty,
                page.Slug ?? string.Empty,
                page.ChapterId == 0 ? null : page.ChapterId,
                page.Priority ?? 0,
                page.Markdown ?? string.Empty,
                tags.FirstOrDefault(t => string.Equals(t.Name, PathTag, StringComparison.OrdinalIgnoreCase))?.Value,
                tags);
        }

        return snapshot;
    }

    // ---- Проход первый: место страницы ----

    /// <summary>
    /// Доводит страницу до нужного места: создаёт или поправляет имя, главу и порядок.
    /// </summary>
    /// <remarks>
    /// Содержимое пишется только при СОЗДАНИИ (пустую страницу BookStack не примет), а у уже
    /// существующей тут не трогается вовсе, и это не разделение ради красоты. Переименование меняет
    /// короткое имя страницы, то есть её адрес; напиши мы ссылки раньше, они указывали бы на прежний
    /// адрес. Поэтому сперва все имена окончательные, и только потом ссылки.
    /// <para>
    /// Страница ищется тремя способами по очереди, и порядок тут существенный:
    /// </para>
    /// <list type="number">
    /// <item>по тегу с путём файла — точное совпадение, переживает переименование заголовка;</item>
    /// <item>среди тех, чей файл исчез, по ИМЕНИ ФАЙЛА без ведущего номера и без каталога. Именно
    /// эта примета переживает оба здешних вида переименования: переезд между каталогами (а по
    /// истории репозитория переезды это подавляющее большинство) и перенумерацию хвоста после
    /// вставки нового шага, при которой меняется и номер в имени файла, и номер в заголовке.
    /// Кандидат берётся только если он ОДИН: два одинаковых имени — это уже гадание, а гадать
    /// адресами страниц нельзя;</item>
    /// <item>там же, по совпадению заголовка — на случай, когда файл переименовали целиком, а
    /// заголовок оставили;</item>
    /// <item>среди вовсе не помеченных, по имени и главе — так забирается страница, заведённая
    /// руками до появления инструмента.</item>
    /// </list>
    /// <para>
    /// Без шагов 2 и 3 переименованный файл заводил бы вторую страницу рядом со старой, а с ключом
    /// <c>--prune</c> — ещё и снос первой, уже отдавшей свой адрес.
    /// </para>
    /// <para>
    /// Занятая страница помечается в <paramref name="claimed"/> и обновляется в снимке. Без этого
    /// два файла с одинаковым заголовком забрали бы одну и ту же страницу, и содержимое первого
    /// молча затёрлось бы содержимым второго.
    /// </para>
    /// </remarks>
    private async Task<RemotePage> PlaceAsync(
        BookStackBook book,
        DocPage page,
        int? chapterId,
        bool ordered,
        Dictionary<int, RemotePage> remote,
        List<RemotePage> stale,
        HashSet<int> claimed,
        CancellationToken ct)
    {
        bool Free(RemotePage r) => !claimed.Contains(r.Id);

        // Единственный кандидат или ничего: выбор «первого попавшегося» из нескольких означал бы,
        // что страницы меняются адресами в зависимости от порядка выдачи сервера.
        static RemotePage? Only(IEnumerable<RemotePage> candidates)
        {
            var found = candidates.Take(2).ToList();
            return found.Count == 1 ? found[0] : null;
        }

        var stem = DocsScanner.FileKey(page.RelPath);

        var match = remote.Values.FirstOrDefault(r => Free(r) && r.DocsPath == page.RelPath)
                    ?? Only(stale.Where(r => Free(r) && DocsScanner.FileKey(r.DocsPath!) == stem))
                    ?? Only(stale.Where(r => Free(r) && r.Name == page.Name))
                    ?? remote.Values.FirstOrDefault(r =>
                        Free(r) && r.DocsPath is null && r.ChapterId == chapterId && r.Name == page.Name);

        if (match is null)
        {
            var created = await portal.Content.CreatePageAsync(new BookStackPageCreate
            {
                BookId = chapterId is null ? book.Id : null,
                ChapterId = chapterId,
                Name = page.Name,
                Markdown = page.Markdown,
                Priority = ordered ? page.Priority : null,
                Tags = [new BookStackTag { Name = PathTag, Value = page.RelPath }],
            }, ct) ?? throw new InvalidOperationException($"Страница «{page.Name}» не создалась.");

            _created++;

            var fresh = new RemotePage(
                created.Id!.Value, created.Name ?? page.Name, created.Slug ?? string.Empty,
                chapterId, page.Priority, created.Markdown ?? page.Markdown, page.RelPath,
                created.Tags ?? []);

            remote[fresh.Id] = fresh;
            claimed.Add(fresh.Id);
            return fresh;
        }

        claimed.Add(match.Id);

        if (match.Name == page.Name
            && match.ChapterId == chapterId
            && match.DocsPath == page.RelPath
            && (!ordered || match.Priority == page.Priority))
        {
            return match;
        }

        var moved = await portal.Content.UpdatePageAsync(match.Id, new BookStackPageUpdate
        {
            BookId = chapterId is null ? book.Id : null,
            ChapterId = chapterId,
            Name = page.Name,
            Priority = ordered ? page.Priority : null,

            // Теги замещаются целиком, поэтому чужие переносятся как есть, а свой переписывается.
            Tags = [
                .. match.Tags.Where(t => !string.Equals(t.Name, PathTag, StringComparison.OrdinalIgnoreCase)),
                new BookStackTag { Name = PathTag, Value = page.RelPath },
            ],
        }, ct) ?? throw new InvalidOperationException($"Страница #{match.Id} не обновилась.");

        // Сказать вслух надо о каждом из четырёх случаев: молчаливая правка чужой страницы это
        // ровно то, чего человек на портале не ждёт.
        if (match.DocsPath is null)
        {
            _adopted++;
            Console.WriteLine($"  забрана страница без метки: «{match.Name}» ← {page.RelPath}");
        }
        else if (match.DocsPath != page.RelPath)
        {
            _adopted++;
            Console.WriteLine($"  файл переехал: {match.DocsPath} → {page.RelPath}");
        }
        else if (match.Name != page.Name || match.ChapterId != chapterId)
        {
            _renamed++;
            Console.WriteLine($"  переставлена: «{match.Name}» → «{page.Name}»");
        }
        else
        {
            _reordered++;
        }

        var settled = match with
        {
            Name = page.Name,
            Slug = moved.Slug ?? match.Slug,
            ChapterId = chapterId,
            Priority = ordered ? page.Priority : match.Priority,
            DocsPath = page.RelPath,
            Tags = moved.Tags ?? match.Tags,
        };

        remote[settled.Id] = settled;
        return settled;
    }

    // ---- Проход второй: содержимое ----

    /// <remarks>
    /// Теги тут НЕ отправляются намеренно. Метку страница получает при расстановке, а посланный
    /// набор тегов BookStack понимает как «пусть будут ровно эти»: любой тег, повешенный человеком
    /// на портале, исчез бы при первой же правке файла, и восстановить его было бы неоткуда.
    /// </remarks>
    private async Task WriteContentAsync(DocPage page, RemotePage remote, CancellationToken ct)
    {
        var result = LinkRewriter.Rewrite(page, page.Markdown, _urls, FileExists);
        _unresolved.AddRange(result.Unresolved);
        _anchors += result.Anchors;

        if (string.Equals(
                BookStackTools.Markdown.NormalizeForCompare(remote.Markdown),
                BookStackTools.Markdown.NormalizeForCompare(result.Markdown),
                StringComparison.Ordinal))
        {
            _unchanged++;
            return;
        }

        await portal.Content.UpdatePageAsync(remote.Id, new BookStackPageUpdate
        {
            Markdown = result.Markdown,
        }, ct);

        _updated++;
    }

    private bool FileExists(string relPath) => DocsPlan.FileExists(args, relPath);

    // ---- Полка и сироты ----

    /// <summary>
    /// Складывает книги на полку, не сбрасывая то, что на ней уже стои́т.
    /// </summary>
    /// <remarks>
    /// Состав полки при обновлении ЗАМЕЩАЕТСЯ целиком, поэтому прежние книги перечисляются заново.
    /// Отправить только свои значило бы снять с полки всё остальное — молча и без единой ошибки.
    /// </remarks>
    private async Task EnsureShelfAsync(IReadOnlyList<int> bookIds, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(args.Shelf))
            return;

        Console.WriteLine();
        Console.Write($"Полка «{args.Shelf}»… ");

        var query = new BookStackListQuery { Count = Window };
        query.Filters[BookStackSortFields.Shelves.Name] = args.Shelf;

        var found = (await portal.Content.ListShelvesAsync(query, ct)).Data
            .FirstOrDefault(s => string.Equals(s.Name, args.Shelf, StringComparison.Ordinal));

        if (found?.Id is null)
        {
            var created = await portal.Content.CreateShelfAsync(
                new BookStackShelfCreate { Name = args.Shelf, Books = bookIds.ToList() }, ct);

            Console.WriteLine($"создана #{created?.Id}, книг {bookIds.Count}");
            return;
        }

        var full = await portal.Content.GetShelfAsync(found.Id.Value, ct);
        var onShelf = full?.Books?.Select(b => b.Id!.Value).ToList() ?? [];
        var merged = onShelf.Concat(bookIds.Where(id => !onShelf.Contains(id))).ToList();

        if (merged.Count == onShelf.Count)
        {
            Console.WriteLine("уже собрана");
            return;
        }

        await portal.Content.UpdateShelfAsync(
            found.Id.Value, new BookStackShelfCreate { Name = full?.Name ?? args.Shelf, Books = merged }, ct);

        Console.WriteLine($"дополнена, книг {merged.Count}");
    }

    /// <summary>
    /// Страницы, у которых был файл, а теперь нет.
    /// </summary>
    /// <remarks>
    /// Сюда попадает только то, что не забрал переименованный файл: усыновление идёт раньше, в
    /// <see cref="PlaceAsync"/>. По умолчанию такие страницы лишь перечисляются. Удаление файла в
    /// репозитории и удаление страницы на портале это разные решения: страницу могли успеть
    /// дописать руками, и снос по умолчанию сделал бы это молча.
    /// </remarks>
    private async Task HandleOrphansAsync(IReadOnlyList<RemotePage> orphans, CancellationToken ct)
    {
        if (orphans.Count == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"Страницы без файла — {orphans.Count}:");

        foreach (var page in orphans)
        {
            Console.WriteLine($"  «{page.Name}» ← {page.DocsPath}");

            if (!args.Prune)
                continue;

            await portal.Content.DeletePageAsync(page.Id, ct);
            Console.WriteLine("    удалена (в корзину)");
        }

        if (!args.Prune)
            Console.WriteLine("  оставлены как есть; снести их — ключ --prune");
    }

    // ---- Отчёт ----

    private void Report()
    {
        Console.WriteLine();
        Console.WriteLine($"Страницы: создано {_created}, забрано у переехавших файлов {_adopted}, "
                          + $"переставлено {_renamed}, переупорядочено {_reordered}, "
                          + $"обновлено {_updated}, без изменений {_unchanged}.");

        if (_anchors > 0)
        {
            Console.WriteLine($"Ссылок на заголовки внутри страниц: {_anchors} — на портале они никуда "
                              + "не ведут, BookStack раздаёт заголовкам свои якоря.");
        }

        if (_unresolved.Count == 0)
        {
            Console.WriteLine("Все относительные ссылки переписаны на адреса портала.");
            return;
        }

        Console.WriteLine($"Ссылки, оставленные как есть — {_unresolved.Count}:");

        foreach (var group in _unresolved.GroupBy(u => u.Reason).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"  {group.Key}: {group.Count()}");

            foreach (var link in group.Take(5))
                Console.WriteLine($"    {link.FromPage} → {link.Target}");

            if (group.Count() > 5)
                Console.WriteLine($"    …и ещё {group.Count() - 5}");
        }
    }
}
