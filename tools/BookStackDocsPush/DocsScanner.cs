using System.Text.RegularExpressions;

namespace BookStackDocsPush;

/// <summary>Страница, собранная из файла на диске.</summary>
/// <param name="RelPath">
/// Путь файла от корня хранилища документов, косыми чертами вперёд. Это ЕДИНСТВЕННОЕ, чем файл
/// связан со страницей на портале: он же уходит тегом и по нему же страница находится в следующий
/// раз, переживая переименование заголовка.
/// </param>
/// <param name="BookKey">Ключ книги: относительный путь корня, из которого пришёл файл.</param>
/// <param name="Chapter">Глава внутри книги либо <c>null</c>, если файл лежит в корне книги.</param>
/// <param name="Name">Заголовок страницы.</param>
/// <param name="Markdown">Тело без ведущего заголовка первого уровня.</param>
/// <param name="Priority">Порядок внутри книги: берётся из сортировки имён файлов.</param>
internal sealed record DocPage(
    string RelPath,
    string BookKey,
    string? Chapter,
    string Name,
    string Markdown,
    int Priority);

/// <summary>
/// Читает дерево markdown-файлов и раскладывает его по уровням BookStack.
/// </summary>
/// <remarks>
/// ВАЖНО: класс только ЧИТАЕТ диск. Ни один путь тут не открывается на запись, и это не случайность,
/// а условие задачи: хранилище документов остаётся нетронутым, вся правка происходит на портале.
/// <para>
/// Уровней у BookStack три (книга, глава, страница), а у дерева каталогов их сколько угодно. Правило
/// приведения одно: глава это подкаталог ПЕРВОГО уровня, а всё, что глубже, попадает в ту же главу,
/// получив путь в имени страницы. Иначе пришлось бы либо терять уровни молча, либо заводить книгу
/// на каждый подкаталог и рассыпать оглавление.
/// </para>
/// </remarks>
internal static class DocsScanner
{
    /// <summary>Заголовок первого уровня в самом начале файла.</summary>
    private static readonly Regex LeadingHeading = new(@"\A﻿?\s*#\s+(?<title>.+?)\s*$", RegexOptions.Multiline);

    /// <summary>Ведущий номер в имени файла: <c>01-Роли.md</c>. Годится на порядок, но не на заголовок.</summary>
    private static readonly Regex LeadingNumber = new(@"^\d+[-_. ]+");

    /// <summary>
    /// Собирает страницы одной книги из каталога.
    /// </summary>
    /// <param name="docsRoot">Корень хранилища документов.</param>
    /// <param name="bookKey">Относительный путь каталога книги внутри хранилища.</param>
    /// <remarks>
    /// Каталоги и файлы, чьё имя начинается с точки, пропускаются: там лежит оснастка репозитория
    /// (<c>.claude</c>, <c>.git</c>), а не документация. Без этого правила в книгу «Архитектура»
    /// приехали бы шесть файлов скиллов, и заметили бы это уже на портале.
    /// </remarks>
    public static IReadOnlyList<DocPage> Scan(string docsRoot, string bookKey)
    {
        var bookDir = Path.Combine(docsRoot, bookKey.Replace('/', Path.DirectorySeparatorChar));
        if (!Directory.Exists(bookDir))
            throw new DirectoryNotFoundException($"Нет каталога {bookDir}.");

        var files = EnumerateMarkdown(bookDir)
            .Select(full => ToRelative(docsRoot, full))
            .OrderBy(rel => rel, StringComparer.Ordinal)
            .ToList();

        var pages = new List<DocPage>(files.Count);

        for (var i = 0; i < files.Count; i++)
        {
            var rel = files[i];
            var text = File.ReadAllText(Path.Combine(docsRoot, rel.Replace('/', Path.DirectorySeparatorChar)));
            var inside = rel[(bookKey.Length + 1)..].Split('/');

            pages.Add(new DocPage(
                RelPath: rel,
                BookKey: bookKey,
                Chapter: inside.Length > 1 ? inside[0] : null,
                Name: BuildName(inside, text),
                Markdown: StripLeadingHeading(text),
                Priority: i));
        }

        return pages;
    }

    /// <summary>
    /// Примета файла, переживающая переименование: имя без каталога, без расширения и без ведущего
    /// номера, в нижнем регистре.
    /// </summary>
    /// <remarks>
    /// По ней страница на портале узнаёт свой файл после переезда. Здешние переименования бывают
    /// двух видов, и оба эту примету не трогают: переезд между каталогами (имя файла остаётся) и
    /// перенумерация хвоста после вставки нового шага (<c>03-Расчёты.md</c> → <c>04-Расчёты.md</c>),
    /// при которой меняются и номер файла, и номер в заголовке, а содержательная часть имени — нет.
    /// <para>
    /// Примета НЕ уникальна: <c>README.md</c> лежит в каждом втором каталоге. Поэтому она годится
    /// только как подсказка, и вызывающий обязан брать кандидата, лишь когда он единственный.
    /// </para>
    /// </remarks>
    public static string FileKey(string relPath)
        => LeadingNumber
            .Replace(Path.GetFileNameWithoutExtension(relPath), string.Empty)
            .Trim()
            .ToLowerInvariant();

    /// <summary>
    /// Имя страницы: заголовок первого уровня, иначе имя файла.
    /// </summary>
    /// <remarks>
    /// Заголовок предпочтительнее имени файла: имена тут вида <c>07-Регистр-и-документ-пополнения.md</c>,
    /// и восстановленное из них название читается хуже написанного человеком. Ведущий номер при этом
    /// не теряется зря: он определяет порядок страниц (см. <see cref="DocPage.Priority"/>).
    /// <para>
    /// Файлы глубже второго уровня получают в имя путь до себя, иначе две страницы с одинаковым
    /// заголовком из разных подкаталогов стали бы неразличимы в оглавлении главы.
    /// </para>
    /// </remarks>
    private static string BuildName(string[] insideBook, string text)
    {
        var heading = LeadingHeading.Match(text);
        var title = heading.Success
            ? heading.Groups["title"].Value.Trim()
            : LeadingNumber.Replace(Path.GetFileNameWithoutExtension(insideBook[^1]), string.Empty)
                .Replace('-', ' ')
                .Trim();

        // Пустое имя портал не примет. Так бывает у файла вроде «01-.md» и у заголовка из одних
        // невидимых знаков: берём имя файла целиком, каким бы оно ни было.
        if (string.IsNullOrWhiteSpace(title))
            title = Path.GetFileNameWithoutExtension(insideBook[^1]);

        // Первый элемент это глава, последний — сам файл. Между ними и есть лишние уровни.
        var deeper = insideBook.Length > 2 ? insideBook[1..^1] : [];

        return deeper.Length == 0 ? title : string.Join('/', deeper) + '/' + title;
    }

    /// <summary>
    /// Убирает ведущий заголовок: на портале он превратился бы во второй такой же прямо под именем
    /// страницы.
    /// </summary>
    /// <remarks>
    /// Кроме одного случая: если кроме заголовка в файле ничего нет, заголовок остаётся. Пустое тело
    /// BookStack не принимает вовсе (отказ на создании), и прогон свалился бы на середине, успев
    /// завести книги, главы и часть страниц. Файл-заглушка из одной строки — вещь обычная, и падать
    /// на ней инструмент не должен.
    /// </remarks>
    private static string StripLeadingHeading(string text)
    {
        var heading = LeadingHeading.Match(text);
        if (!heading.Success || heading.Index != 0)
            return text;

        var body = text[(heading.Index + heading.Length)..].TrimStart('\r', '\n');

        return string.IsNullOrWhiteSpace(body) ? text : body;
    }

    private static IEnumerable<string> EnumerateMarkdown(string dir)
    {
        foreach (var file in Directory.EnumerateFiles(dir, "*.md"))
        {
            if (!Path.GetFileName(file).StartsWith('.'))
                yield return file;
        }

        foreach (var sub in Directory.EnumerateDirectories(dir))
        {
            if (Path.GetFileName(sub).StartsWith('.'))
                continue;

            foreach (var file in EnumerateMarkdown(sub))
                yield return file;
        }
    }

    private static string ToRelative(string root, string full)
        => Path.GetRelativePath(root, full).Replace('\\', '/');
}
