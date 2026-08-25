using BookStackTools;

namespace BookStackDocsPush.Tests;

/// <summary>
/// Приведение текста к сравнимому виду и обход блоков кода.
/// </summary>
/// <remarks>
/// Обе вещи тихие: ошибка тут не падает, а либо переписывает портал целиком на каждом прогоне,
/// либо портит команду в инструкции. Проверить их вживую нельзя — нужен портал, — поэтому они
/// вынесены отдельно и закрыты здесь.
/// </remarks>
public sealed class MarkdownTests
{
    [Fact]
    public void NormalizeForCompare_IgnoresLineEndingStyle()
    {
        var windows = "Первая\r\nВторая\r\n";
        var unix = "Первая\nВторая\n";

        BookStackTools.Markdown.NormalizeForCompare(windows)
            .Should().Be(BookStackTools.Markdown.NormalizeForCompare(unix),
                "файл на диске хранится с CRLF, а портал отдаёт что у него лежит");
    }

    [Fact]
    public void NormalizeForCompare_TrimsBothEnds()
    {
        BookStackTools.Markdown.NormalizeForCompare("\n\n> Текст\n\n")
            .Should().Be("> Текст",
                "приложение на той стороне подрезает присланное с обоих концов, и текст, начатый "
                + "с пустой строки, иначе не совпал бы сам с собой ни разу");
    }

    [Theory]
    [InlineData("﻿Текст")]
    [InlineData("​Текст")]
    [InlineData("Текст‎")]
    public void NormalizeForCompare_DropsInvisibleEdges(string text)
    {
        BookStackTools.Markdown.NormalizeForCompare(text)
            .Should().Be("Текст",
                "для .NET это не пробелы, а сервер их срезает — иначе страница переписывалась бы вечно");
    }

    [Fact]
    public void MapLinesOutsideFences_SkipsFencedBlocks()
    {
        var text = string.Join('\n',
            "адрес",
            "```bash",
            "адрес",
            "```",
            "адрес");

        var result = BookStackTools.Markdown.MapLinesOutsideFences(text, line => line.Replace("адрес", "НОВЫЙ"));

        result.Should().Be(string.Join('\n', "НОВЫЙ", "```bash", "адрес", "```", "НОВЫЙ"),
            "внутри блока адрес это часть команды, и подмена превратила бы инструкцию в неверную");
    }

    [Fact]
    public void MapLinesOutsideFences_KeepsCarriageReturns()
    {
        var text = "первая\r\nвторая\r\n";

        BookStackTools.Markdown.MapLinesOutsideFences(text, line => line)
            .Should().Be(text, "правка одной ссылки не повод переписывать переводы строк всего документа");
    }
}
