using BookStackSdk.Abstractions;
using BookStackSdk.Errors;
using BookStackSdk.Models;
using BookStackTools;

namespace BookStackShelfSync;

/// <summary>
/// Перенос полки: её книги уезжают архивами, сама полка собирается на приёмнике заново.
/// </summary>
/// <remarks>
/// Полка не переносится и не может: маршрута выгрузки у полок нет вовсе, есть только у книг, глав
/// и страниц. То, что снаружи выглядит переносом полки, внутри всегда «перенести книги и сложить
/// из них полку», и об этом стоит помнить, читая вывод: пока не собрана полка, книги на приёмнике
/// уже есть, просто лежат сами по себе.
/// </remarks>
internal sealed class ShelfSync(BookStackEndpoint source, BookStackEndpoint target, SyncArgs args)
{
    /// <summary>Окно списка при поиске по слагу. Слаг уникален, но фильтр может отвалиться молча.</summary>
    private const int LookupWindow = 500;

    public async Task RunAsync(CancellationToken ct)
    {
        var shelf = await ReadSourceShelfAsync(ct);
        var books = shelf.Books ?? [];

        Console.WriteLine($"Полка: {shelf.Name} ({args.ShelfSlug}), книг: {books.Count}");
        foreach (var book in books)
            Console.WriteLine($"  - {book.Name} [{book.Slug}]");

        var existing = await ReadTargetStateAsync(books, ct);
        var targetShelf = await FindShelfAsync(target, args.ShelfSlug, ct);

        Console.WriteLine();
        Console.WriteLine($"Приёмник {target.BaseUrl}:");
        Console.WriteLine(targetShelf is null
            ? "  полки нет, будет создана"
            : $"  полка есть (#{targetShelf.Id}), состав будет замещён");

        foreach (var (slug, book) in existing)
        {
            Console.WriteLine(args.Replace
                ? $"  книга [{slug}] есть (#{book.Id}) — будет снесена и залита заново"
                : $"  книга [{slug}] есть (#{book.Id}) — ОСТАНЕТСЯ, рядом ляжет дубль");
        }

        if (args.DryRun)
        {
            Console.WriteLine();
            Console.WriteLine("--dry-run: ничего не менял.");
            return;
        }

        if (existing.Count > 0 && !args.Replace)
        {
            Console.WriteLine();
            Console.WriteLine("ВНИМАНИЕ: импорт всегда создаёт новое. Без --replace будут дубли.");
        }

        Directory.CreateDirectory(args.WorkDir);
        Console.WriteLine();
        Console.WriteLine($"Архивы: {args.WorkDir}");

        var moved = new List<BookStackBook>(books.Count);
        var refused = new List<(BookStackBook Book, string Reason)>();

        foreach (var book in books)
        {
            ct.ThrowIfCancellationRequested();

            // Отказ по одной книге не должен уносить весь перенос: остальные девять доедут, а
            // разбираться с этой (обычно это предел размера на сервере) человек будет отдельно.
            try
            {
                moved.Add(await MoveBookAsync(book, existing, ct));
            }
            catch (BookStackApiException e)
            {
                Console.WriteLine($"  НЕ ПЕРЕЕХАЛА: {Explain(e)}");
                refused.Add((book, Explain(e)));
            }
        }

        if (moved.Count > 0)
        {
            var shelfId = await BuildShelfAsync(shelf, targetShelf, moved, ct);
            await CopyShelfCoverAsync(shelf, shelfId, ct);
            await CheckLinksAsync(moved, ct);
        }

        Console.WriteLine();

        if (refused.Count > 0)
        {
            Console.WriteLine($"Перенеслось книг {moved.Count} из {books.Count}. Не переехали:");

            foreach (var (book, reason) in refused)
                Console.WriteLine($"  - {book.Name} [{book.Slug}]: {reason}");

            Console.WriteLine();
            Console.WriteLine(
                "Полка собрана из того, что доехало. Разберитесь с причиной и повторите прогон: "
                + "с --replace уже перенесённые книги заменятся, дублей не будет.");

            throw new PartialTransferException(moved.Count, books.Count);
        }

        Console.WriteLine($"Готово: {target.BaseUrl}/shelves/{args.ShelfSlug}");
    }

    /// <summary>
    /// Расшифровывает отказ сервера там, где его текст сам по себе мало что говорит.
    /// </summary>
    /// <remarks>
    /// Про размер стои́т сказать прямо: BookStack отвечает 422 и «сервер может не принимать файлы
    /// такого размера», и по этой фразе человек идёт искать ошибку в BookStack, тогда как предел
    /// стои́т в PHP (<c>upload_max_filesize</c>, <c>post_max_size</c>) и в веб-сервере
    /// (<c>client_max_body_size</c>), то есть чинится не там, где написано.
    /// </remarks>
    private static string Explain(BookStackApiException error)
    {
        var text = error.Message;

        return text.Contains("could not be uploaded", StringComparison.OrdinalIgnoreCase)
               || text.Contains("files of this size", StringComparison.OrdinalIgnoreCase)
            ? "архив не принят по размеру. Предел задан НЕ в BookStack: это upload_max_filesize и "
              + "post_max_size в PHP плюс client_max_body_size в веб-сервере на приёмнике"
            : text;
    }

    // ---- Источник ----

    private async Task<BookStackShelf> ReadSourceShelfAsync(CancellationToken ct)
    {
        var brief = await FindShelfAsync(source, args.ShelfSlug, ct)
            ?? throw new InvalidOperationException(
                $"Полки [{args.ShelfSlug}] на {source.BaseUrl} нет, либо токену её не видно.");

        // Состав полки приходит только при чтении одиночной: в списке его нет.
        var shelf = await source.Content.GetShelfAsync(brief.Id!.Value, ct)
            ?? throw new InvalidOperationException($"Полка #{brief.Id} не читается.");

        if (shelf.Books is null || shelf.Books.Count == 0)
        {
            throw new InvalidOperationException(
                "На полке нет видимых книг. Либо она пуста, либо токен источника видит не всё, "
                + "и тогда перенос был бы молча неполным.");
        }

        return shelf;
    }

    /// <summary>Что из переносимого уже лежит на приёмнике, по слагам книг.</summary>
    private async Task<Dictionary<string, BookStackBook>> ReadTargetStateAsync(
        IReadOnlyList<BookStackBook> books, CancellationToken ct)
    {
        var found = new Dictionary<string, BookStackBook>(StringComparer.Ordinal);

        foreach (var book in books)
        {
            if (string.IsNullOrWhiteSpace(book.Slug))
                continue;

            var hit = await FindBookAsync(target, book.Slug, ct);
            if (hit is not null)
                found[book.Slug] = hit;
        }

        return found;
    }

    // ---- Книги ----

    private async Task<BookStackBook> MoveBookAsync(
        BookStackBook book, IReadOnlyDictionary<string, BookStackBook> existing, CancellationToken ct)
    {
        var slug = book.Slug ?? book.Id!.Value.ToString();

        Console.WriteLine();
        Console.WriteLine($"> {book.Name}");

        Console.Write("  выгрузка… ");
        var zip = await source.Export.ExportBookAsync(book.Id!.Value, BookStackExportFormat.Zip, ct);
        Console.WriteLine($"{zip.Content.Length / 1024d / 1024d:F1} МБ");

        // Архив кладётся на диск ДО импорта и остаётся после него. Стоит это ничего, а спасает от
        // повторной выгрузки, если импорт упадёт на середине.
        var path = Path.Combine(args.WorkDir, slug + ".zip");
        await File.WriteAllBytesAsync(path, zip.Content, ct);

        BookStackBook? replaced = null;
        if (args.Replace && existing.TryGetValue(slug, out var old))
        {
            Console.Write($"  снос прежней #{old.Id}… ");
            await target.Content.DeleteBookAsync(old.Id!.Value, ct);
            Console.WriteLine("в корзину");
            replaced = old;
        }

        Console.Write("  загрузка… ");
        var import = await target.Import.UploadAsync(Path.GetFileName(path), zip.Content, ct)
            ?? throw new InvalidOperationException("Сервер не вернул запись импорта.");
        Console.WriteLine($"импорт #{import.Id}");

        Console.Write("  разбор… ");
        var created = await target.Import.RunAsBookAsync(import.Id!.Value, ct)
            ?? throw new InvalidOperationException($"Импорт #{import.Id} не вернул книгу.");
        Console.WriteLine($"книга #{created.Id} [{created.Slug}]");

        if (replaced is not null)
            await PurgeAsync(replaced, ct);

        if (!string.Equals(created.Slug, slug, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"  ВНИМАНИЕ: слаг разошёлся ({slug} → {created.Slug}), адреса книги на источнике "
                + "и на приёмнике будут разными. Обычно это значит, что слаг занят живой книгой: "
                + "лежащая в корзине его не держит.");
        }

        return created;
    }

    /// <summary>
    /// Добивает из корзины книгу, снесённую перед импортом.
    /// </summary>
    /// <remarks>
    /// ПОРЯДОК ВАЖЕН, и он такой: мягкое удаление ДО импорта, добивание ПОСЛЕ. Удалять до нужно
    /// затем, чтобы на приёмнике не оказалось двух книг сразу; добивать после — затем, что упавший
    /// импорт иначе оставил бы приёмник вовсе без книги, а так она лежит в корзине и возвращается
    /// оттуда. Между этими двумя моментами приёмник не теряет ничего безвозвратно.
    /// <para>
    /// Слаг тут ни при чём: мягко удалённая книга его НЕ держит, генератор коротких имён удалённых
    /// не видит, и импортированная взамен получает ровно то же имя (замер SDK, см.
    /// <see cref="BookStackSdk.Abstractions.IBookStackRecycleBinApi"/>). Добивание нужно ради того,
    /// чтобы не осталась мина: восстановление не проверяет, свободно ли короткое имя, и «верну,
    /// посмотрю, что было» после переноса даёт ДВЕ книги с одним слагом.
    /// </para>
    /// <para>
    /// Права на корзину есть не у всякого токена (нужны сразу два: управление настройками и
    /// управление правами), поэтому отказ тут перенос не валит: книга переехала, адреса совпали,
    /// в корзине осталась запись, и об этом сказано вслух.
    /// </para>
    /// </remarks>
    private async Task PurgeAsync(BookStackBook book, CancellationToken ct)
    {
        Console.Write("  добивание прежней из корзины… ");

        try
        {
            var deletionId = await FindDeletionAsync(book.Id!.Value, ct);
            if (deletionId is null)
            {
                Console.WriteLine("в корзине не нашлась, добейте руками");
                return;
            }

            var destroyed = await target.RecycleBin.DestroyAsync(deletionId.Value, ct);
            Console.WriteLine($"насовсем, записей {destroyed?.DeleteCount}");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Console.WriteLine($"корзину добить не вышло ({e.Message.Trim()}), запись осталась там");
        }
    }

    /// <summary>Запись корзины для только что удалённой книги.</summary>
    /// <remarks>
    /// Ищется перебором: фильтровать корзину по удалённому объекту нечем, поле <c>deletable_id</c>
    /// в фильтры не заявлено. Свежая запись лежит в конце по умолчанию, поэтому берётся сортировка
    /// по убыванию идентификатора и одно окно: если книги нет в первых пяти сотнях свежих записей,
    /// значит удалял не этот вызов, и добивать чужое мы не станем.
    /// </remarks>
    private async Task<int?> FindDeletionAsync(int bookId, CancellationToken ct)
    {
        var bin = await target.RecycleBin.ListAsync(LookupWindow, offset: null, sort: "-id", ct);

        return bin.Data
            .FirstOrDefault(item =>
                string.Equals(item.DeletableType, "book", StringComparison.OrdinalIgnoreCase)
                && item.DeletableId == bookId)?
            .Id;
    }

    // ---- Полка ----

    private async Task<int> BuildShelfAsync(
        BookStackShelf shelf, BookStackShelf? existing, IReadOnlyList<BookStackBook> books, CancellationToken ct)
    {
        Console.WriteLine();
        Console.Write("Полка… ");

        var body = new BookStackShelfCreate
        {
            Name = shelf.Name,
            Tags = shelf.Tags,

            // Описание берётся тем же полем, каким оно есть на источнике: простой текст рядом с
            // HTML сервер всё равно перебьёт вторым, а вот HTML, отправленный как простой текст,
            // приедет разметкой напоказ.
            Description = string.IsNullOrWhiteSpace(shelf.DescriptionHtml) ? shelf.Description : null,
            DescriptionHtml = shelf.DescriptionHtml,

            // Порядок списка становится порядком книг на полке, поэтому книги идут в том же
            // порядке, в каком стояли на источнике.
            Books = books.Select(b => b.Id!.Value).ToList(),
        };

        var saved = existing is null
            ? await target.Content.CreateShelfAsync(body, ct)
            : await target.Content.UpdateShelfAsync(existing.Id!.Value, body, ct);

        if (saved?.Id is null)
            throw new InvalidOperationException("Сервер не вернул полку.");

        Console.WriteLine($"#{saved.Id} [{saved.Slug}], книг {books.Count}");
        return saved.Id.Value;
    }

    /// <summary>
    /// Переносит обложку полки, если она есть.
    /// </summary>
    /// <remarks>
    /// Отдельным шагом, потому что обложка уходит многочастным телом, а не полем полки. Обложки
    /// КНИГ переносить не надо: они лежат внутри архива и приезжают вместе с книгой.
    /// </remarks>
    private async Task CopyShelfCoverAsync(BookStackShelf shelf, int targetShelfId, CancellationToken ct)
    {
        var url = shelf.Cover?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return;

        Console.Write("Обложка полки… ");
        try
        {
            var image = await source.Uploads.GetImageDataByUrlAsync(url, ct);
            var name = shelf.Cover?.Name ?? image.FileName ?? "cover.png";

            await target.Uploads.SetShelfCoverAsync(targetShelfId, name, image.Content, image.ContentType, ct);
            Console.WriteLine("перенесена");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Console.WriteLine($"не перенеслась ({e.Message.Trim()}), поставьте руками");
        }
    }

    // ---- Ссылки ----

    /// <summary>
    /// Ищет в перенесённых страницах ссылки на источник и, если просили, правит их.
    /// </summary>
    /// <remarks>
    /// Проверка идёт всегда, а правка только по <c>--rewrite-links</c>: ссылка на источник это не
    /// обязательно ошибка (бывают и осмысленные), а вот незамеченной она быть не должна — с виду
    /// перенос удался, а половина переходов уводит обратно на боевой портал.
    /// <para>
    /// ВАЖНО про то, что правится. Обновление страницы в SDK идёт markdown-ом, поэтому страницы,
    /// написанные визуальным редактором (у них пустой <c>markdown</c> и заполненный <c>html</c>),
    /// править нечем: замена в HTML означала бы отправить его полем markdown и превратить разметку
    /// в текст. Такие страницы только перечисляются.
    /// </para>
    /// <para>
    /// Огороженные блоки кода не правятся ТОЖЕ, и это не упущение: внутри них адрес это часть
    /// команды или примера настройки, а не переход. Подменённый там адрес превращает верную
    /// инструкцию в неверную, оставляя её на вид рабочей. Страница, где после правки адрес
    /// источника всё же остался, попадает в перечень для человека.
    /// </para>
    /// </remarks>
    private async Task CheckLinksAsync(IReadOnlyList<BookStackBook> books, CancellationToken ct)
    {
        Console.WriteLine();
        Console.WriteLine($"Проверка ссылок на {source.BaseUrl}…");

        var fixedPages = 0;
        var manual = new List<string>();

        foreach (var book in books)
        {
            var query = new BookStackListQuery { Count = LookupWindow };
            query.Filters[BookStackSortFields.Pages.BookId] = book.Id!.Value.ToString();

            await foreach (var brief in target.Content.EnumeratePagesAsync(query, ct))
            {
                // В списке нет ни markdown, ни html: за содержимым надо идти чтением одиночной.
                var page = await target.Content.GetPageAsync(brief.Id!.Value, ct);
                if (page is null)
                    continue;

                var markdown = page.Markdown;
                var hasInMarkdown = markdown?.Contains(source.BaseUrl, StringComparison.OrdinalIgnoreCase) == true;
                var hasInHtml = page.Html?.Contains(source.BaseUrl, StringComparison.OrdinalIgnoreCase) == true;

                if (!hasInMarkdown && !hasInHtml)
                    continue;

                // Замена не заходит в огороженные блоки: там адрес источника это часть команды или
                // примера, а не переход, и подмена испортила бы инструкцию, оставив её на вид рабочей.
                var patched = hasInMarkdown
                    ? BookStackTools.Markdown.MapLinesOutsideFences(
                        markdown!, line => line.Replace(source.BaseUrl, target.BaseUrl, StringComparison.OrdinalIgnoreCase))
                    : null;

                // Осталось после правки (или править нечем) — в отчёт: адрес мог сидеть в блоке кода
                // либо в HTML-странице, и оба случая просят человека.
                if (!args.RewriteLinks || patched is null
                    || patched.Contains(source.BaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    manual.Add($"{book.Slug}/page/{page.Slug}");

                    if (!args.RewriteLinks || patched is null || patched == markdown)
                        continue;
                }

                await target.Content.UpdatePageAsync(
                    page.Id!.Value,
                    new BookStackPageUpdate { Markdown = patched },
                    ct);

                fixedPages++;
            }
        }

        if (fixedPages > 0)
            Console.WriteLine($"  поправлено страниц: {fixedPages}");

        if (manual.Count > 0)
        {
            Console.WriteLine(args.RewriteLinks
                ? $"  правкой не берутся (не markdown либо адрес внутри блока кода) — {manual.Count}:"
                : $"  ссылки на источник остались, для правки нужен --rewrite-links — {manual.Count}:");

            foreach (var page in manual)
                Console.WriteLine($"    {target.BaseUrl}/books/{page}");
        }

        if (fixedPages == 0 && manual.Count == 0)
            Console.WriteLine("  ссылок на источник нет");
    }

    // ---- Поиск по слагу ----

    /// <remarks>
    /// Слаг проверяется ещё раз уже по ответу, и это не перестраховка: неизвестное имя фильтра
    /// BookStack выбрасывает МОЛЧА и отдаёт весь список. Без сверки первая попавшаяся полка сошла
    /// бы за искомую.
    /// </remarks>
    private static async Task<BookStackShelf?> FindShelfAsync(
        BookStackEndpoint endpoint, string slug, CancellationToken ct)
    {
        var query = new BookStackListQuery { Count = LookupWindow };
        query.Filters[BookStackSortFields.Shelves.Slug] = slug;

        var found = await endpoint.Content.ListShelvesAsync(query, ct);
        return found.Data.FirstOrDefault(s => string.Equals(s.Slug, slug, StringComparison.Ordinal));
    }

    private static async Task<BookStackBook?> FindBookAsync(
        BookStackEndpoint endpoint, string slug, CancellationToken ct)
    {
        var query = new BookStackListQuery { Count = LookupWindow };
        query.Filters[BookStackSortFields.Books.Slug] = slug;

        var found = await endpoint.Content.ListBooksAsync(query, ct);
        return found.Data.FirstOrDefault(b => string.Equals(b.Slug, slug, StringComparison.Ordinal));
    }
}

/// <summary>
/// Перенос дошёл до конца, но часть книг осталась дома.
/// </summary>
/// <remarks>
/// Отдельный тип нужен ради кода возврата: молча выйти нулём после того, как две книги из десяти
/// не переехали, значит соврать вызывающему скрипту, а свалиться на первой же неудаче — бросить
/// остальные восемь.
/// </remarks>
internal sealed class PartialTransferException(int moved, int total)
    : Exception($"Перенеслось книг {moved} из {total}.")
{
    public int Moved { get; } = moved;

    public int Total { get; } = total;
}
