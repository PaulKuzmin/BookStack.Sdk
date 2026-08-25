using BookStackDocsPush;

namespace BookStackDocsPush.Tests;

/// <summary>
/// Переписывание ссылок между документами.
/// </summary>
/// <remarks>
/// Самое опасное место инструмента: ссылок почти семь сотен, они уходят в текст страниц, и ошибка
/// тут не падает, а тихо уводит читателя не туда. Поэтому проверяется и то, что переписывается, и
/// то, что переписываться НЕ должно.
/// </remarks>
public sealed class LinkRewriterTests
{
    private static readonly DocPage From = new(
        RelPath: "Архитектура/Разделы/01-Контрагенты.md",
        BookKey: "Архитектура",
        Chapter: "Разделы",
        Name: "Контрагенты",
        Markdown: string.Empty,
        Priority: 0);

    private static readonly Dictionary<string, string> Urls = new(StringComparer.Ordinal)
    {
        ["Архитектура/Архитектура.md"] = "https://test.help.altway.pro/books/arh/page/arhitektura",
        ["Архитектура/Разделы"] = "https://test.help.altway.pro/books/arh/chapter/razdely",
    };

    private static RewriteResult Run(string markdown, Func<string, bool>? exists = null)
        => LinkRewriter.Rewrite(From, markdown, Urls, exists ?? (_ => false));

    [Fact]
    public void Rewrite_ReplacesRelativeLinkWithPortalAddress()
    {
        var result = Run("См. [архитектуру](../Архитектура.md) целиком.");

        result.Markdown.Should().Be(
            "См. [архитектуру](https://test.help.altway.pro/books/arh/page/arhitektura) целиком.");
        result.Rewritten.Should().Be(1);
        result.Unresolved.Should().BeEmpty();
    }

    [Fact]
    public void Rewrite_ResolvesLinkToFolder_AsChapter()
    {
        var result = Run("Смотри [разделы](../Разделы).");

        result.Markdown.Should().Contain("/chapter/razdely", "каталог первого уровня стал главой");
        result.Rewritten.Should().Be(1);
    }

    [Fact]
    public void Rewrite_DropsAnchor_ButKeepsTitle()
    {
        var result = Run("""[раздел](../Архитектура.md#4-1-kontragenty "подсказка")""");

        result.Markdown.Should().Be(
            """[раздел](https://test.help.altway.pro/books/arh/page/arhitektura "подсказка")""");
        result.Markdown.Should().NotContain("#4-1", "якоря BookStack раздаёт свои, наш перенести нечем");
    }

    [Theory]
    [InlineData("[внешняя](https://example.com/a.md)")]
    [InlineData("[почта](mailto:pavel@example.com)")]
    [InlineData("[без схемы](//example.com/a.md)")]
    public void Rewrite_LeavesNonRelativeLinksAlone(string markdown)
    {
        var result = Run(markdown);

        result.Markdown.Should().Be(markdown);
        result.Rewritten.Should().Be(0);
        result.Unresolved.Should().BeEmpty("это не наши ссылки, и в отчёт им попадать незачем");
    }

    [Fact]
    public void Rewrite_CountsInPageAnchors_Separately()
    {
        var result = Run("Смотри [ниже](#глава-2) и [ещё ниже](#глава-3).");

        result.Markdown.Should().Contain("(#глава-2)", "переписать такую ссылку нечем");
        result.Anchors.Should().Be(2,
            "на портале свои якоря у заголовков, и об этих ссылках надо сказать вслух, а не молчать");
        result.Unresolved.Should().BeEmpty("это не ссылки на файлы, им в перечень битых не место");
    }

    [Fact]
    public void Rewrite_LeavesFencedCodeAlone()
    {
        var markdown = """
            Текст с [ссылкой](../Архитектура.md).

            ```markdown
            [пример](../Архитектура.md)
            ```
            """;

        var result = Run(markdown);

        result.Rewritten.Should().Be(1, "внутри огороженного блока путь это пример, а не переход");
        result.Markdown.Should().Contain("[пример](../Архитектура.md)");
    }

    [Fact]
    public void Rewrite_ReportsLinkOutsideTransferredTrees()
    {
        var result = Run("[исходник](../../Стиль/Палитра.md)", exists: _ => true);

        result.Rewritten.Should().Be(0);
        result.Unresolved.Should().ContainSingle()
            .Which.Reason.Should().Be("файл есть, но он вне переносимых каталогов");
    }

    [Fact]
    public void Rewrite_ReportsMissingFile()
    {
        var result = Run("[пропажа](Нет-такого.md)");

        result.Unresolved.Should().ContainSingle().Which.Reason.Should().Be("файла нет");
    }

    [Fact]
    public void Rewrite_ReportsPathAboveRoot()
    {
        var result = Run("[исходник](../../../../AltWayService/Program.cs)");

        result.Unresolved.Should().ContainSingle()
            .Which.Reason.Should().Be("путь уходит выше корня хранилища");
    }

    [Fact]
    public void Rewrite_LeavesTextIntact_WhenNothingMatches()
    {
        const string markdown = "Просто текст без ссылок.\nВторая строка.";

        Run(markdown).Markdown.Should().Be(markdown, "переносы строк не наше дело, их сравнивает портал");
    }
}
