using System.Text.RegularExpressions;
using BookStackTools;

namespace BookStackDocsPush;

/// <summary>Ссылка, которую переписать не вышло.</summary>
/// <param name="FromPage">Путь файла, где она стои́т.</param>
/// <param name="Target">Как она записана в тексте.</param>
/// <param name="Reason">Почему оставлена как есть.</param>
internal sealed record UnresolvedLink(string FromPage, string Target, string Reason);

/// <summary>Итог переписывания одного документа.</summary>
/// <param name="Markdown">Текст после замен.</param>
/// <param name="Rewritten">Сколько ссылок заменено.</param>
/// <param name="Unresolved">Что осталось как было.</param>
/// <param name="Anchors">
/// Сколько встретилось ссылок на заголовок внутри той же страницы. Переписать их нечем: BookStack
/// раздаёт заголовкам собственные якоря (<c>bkmrk-…</c>), и на портале такие переходы никуда не
/// ведут. Молчать об этом нельзя, поэтому они считаются отдельно от всего прочего.
/// </param>
internal sealed record RewriteResult(
    string Markdown, int Rewritten, IReadOnlyList<UnresolvedLink> Unresolved, int Anchors);

/// <summary>
/// Переписывает относительные ссылки между документами на адреса страниц портала.
/// </summary>
/// <remarks>
/// Зачем вообще: в хранилище документы ссылаются друг на друга путями (<c>../Разделы/01-Контрагенты.md</c>).
/// На портале таких путей нет, и ссылка, приехавшая как есть, ведёт в никуда. Их почти семь сотен,
/// то есть «поправим потом руками» тут не работает.
/// <para>
/// Переписывается ТОЛЬКО то, что удалось сопоставить со страницей: ссылки на файлы вне переносимых
/// деревьев (исходники, брендбук, архив) и на несуществующие пути остаются нетронутыми и попадают в
/// отчёт. Подменить их «похожим» адресом было бы хуже поломки: битая ссылка видна, а уводящая не туда
/// притворяется рабочей.
/// </para>
/// <para>
/// Содержимое огороженных блоков кода не трогается, см. <see cref="BookStackTools.Markdown"/>.
/// </para>
/// </remarks>
internal static class LinkRewriter
{
    /// <summary>Ссылка markdown: <c>[текст](цель "подсказка")</c>. Картинки сюда же попадают.</summary>
    private static readonly Regex Link = new(@"\]\(\s*(?<target>[^)\s]+)(?<title>\s+""[^""]*"")?\s*\)");

    /// <summary>
    /// Переписывает ссылки одного документа.
    /// </summary>
    /// <param name="page">Документ, чьи ссылки правятся.</param>
    /// <param name="markdown">Текст документа.</param>
    /// <param name="urls">Адреса страниц портала по путям файлов.</param>
    /// <param name="fileExists">
    /// Проверка существования файла по пути от корня хранилища. Нужна только для отчёта: она
    /// отличает «файл есть, но мы его не переносим» от «ссылка ведёт в пустоту», а это разные
    /// поводы для беспокойства.
    /// </param>
    public static RewriteResult Rewrite(
        DocPage page,
        string markdown,
        IReadOnlyDictionary<string, string> urls,
        Func<string, bool> fileExists)
    {
        var unresolved = new List<UnresolvedLink>();
        var rewritten = 0;
        var anchors = 0;

        var result = BookStackTools.Markdown.MapLinesOutsideFences(markdown, line =>
            Link.Replace(line, match =>
            {
                var target = match.Groups["target"].Value;
                var title = match.Groups["title"].Value;

                // Ссылка на заголовок внутри той же страницы. Тронуть её нечем — якоря BookStack
                // раздаёт свои, — но и промолчать нельзя: на портале она никуда не ведёт.
                if (target.StartsWith('#'))
                {
                    anchors++;
                    return match.Value;
                }

                if (!IsRelative(target))
                    return match.Value;

                var (path, _) = Split(target);
                var resolved = Resolve(page.RelPath, path);

                if (resolved is not null && urls.TryGetValue(resolved, out var url))
                {
                    rewritten++;

                    // Якорь отбрасывается намеренно: заголовкам BookStack раздаёт свои
                    // идентификаторы, и перенести наш якорь в них нечем. Ссылка на страницу целиком
                    // доводит читателя до места, выдуманный якорь — никуда.
                    return $"]({url}{title})";
                }

                unresolved.Add(new UnresolvedLink(
                    page.RelPath,
                    target,
                    resolved is null ? "путь уходит выше корня хранилища"
                        : fileExists(resolved) ? "файл есть, но он вне переносимых каталогов"
                        : "файла нет"));

                return match.Value;
            }));

        return new RewriteResult(result, rewritten, unresolved, anchors);
    }

    /// <summary>Отделяет якорь от пути.</summary>
    private static (string Path, string Fragment) Split(string value)
    {
        var hash = value.IndexOf('#');
        return hash < 0 ? (value, string.Empty) : (value[..hash], value[hash..]);
    }

    /// <summary>Ссылка ли это на соседний файл, а не наружу.</summary>
    private static bool IsRelative(string target)
        => target.Length > 0
           && !target.StartsWith('#')
           && !target.StartsWith("//", StringComparison.Ordinal)
           && !target.Contains("://", StringComparison.Ordinal)
           && !target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Приводит относительный путь к пути от корня хранилища.
    /// </summary>
    /// <remarks>
    /// Собственная сборка вместо <see cref="Path.GetFullPath(string)"/>: тот привязан к текущему
    /// каталогу и к разделителям хозяйской системы, а нам нужен ровно тот же ключ, каким страницы
    /// сложены в карту — от корня хранилища и косыми чертами вперёд.
    /// </remarks>
    /// <returns>Путь от корня либо <c>null</c>, если ссылка уходит выше корня.</returns>
    private static string? Resolve(string fromRelPath, string target)
    {
        // Путь от корня хранилища начинается с косой черты; всё прочее считается от каталога файла.
        var parts = target.StartsWith('/')
            ? new List<string>()
            : [.. fromRelPath.Split('/')[..^1]];

        foreach (var piece in Uri.UnescapeDataString(target.Replace('\\', '/')).Split('/'))
        {
            switch (piece)
            {
                case "" or ".":
                    continue;
                case "..":
                    if (parts.Count == 0)
                        return null;

                    parts.RemoveAt(parts.Count - 1);
                    break;
                default:
                    parts.Add(piece);
                    break;
            }
        }

        return parts.Count == 0 ? null : string.Join('/', parts);
    }
}
