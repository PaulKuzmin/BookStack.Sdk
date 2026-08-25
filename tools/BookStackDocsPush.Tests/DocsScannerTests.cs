using BookStackDocsPush;

namespace BookStackDocsPush.Tests;

/// <summary>
/// Разбор дерева каталогов в страницы.
/// </summary>
/// <remarks>
/// Проверяется на настоящем дереве во временном каталоге, а не на выдуманной прослойке: правила тут
/// про файловую систему (точки в именах, вложенность, сортировка), и заглушка проверяла бы наше
/// представление о ней, а не её саму.
/// </remarks>
public sealed class DocsScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "docs-scanner-" + Guid.NewGuid().ToString("N"));

    private void Write(string relPath, string text)
    {
        var full = Path.Combine(_root, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Scan_TakesNameFromHeading_AndDropsItFromBody()
    {
        Write("Книга/01-Роли.md", "# Роли и права\n\nТекст раздела.\n");

        var page = DocsScanner.Scan(_root, "Книга").Should().ContainSingle().Subject;

        page.Name.Should().Be("Роли и права");
        page.Markdown.Should().Be("Текст раздела.\n",
            "заголовок остался бы на странице вторым таким же, прямо под её именем");
    }

    [Fact]
    public void Scan_WithoutHeading_TakesNameFromFile_WithoutLeadingNumber()
    {
        Write("Книга/07-Регистр-и-документ.md", "Сразу текст, без заголовка.\n");

        var page = DocsScanner.Scan(_root, "Книга").Should().ContainSingle().Subject;

        page.Name.Should().Be("Регистр и документ");
        page.Markdown.Should().StartWith("Сразу текст");
    }

    [Fact]
    public void Scan_FirstLevelFolder_BecomesChapter()
    {
        Write("Книга/README.md", "# Оглавление\n");
        Write("Книга/Разделы/01-Контрагенты.md", "# Контрагенты\n");

        var pages = DocsScanner.Scan(_root, "Книга");

        pages.Should().HaveCount(2);
        pages.Single(p => p.Name == "Оглавление").Chapter.Should().BeNull();
        pages.Single(p => p.Name == "Контрагенты").Chapter.Should().Be("Разделы");
    }

    [Fact]
    public void Scan_DeeperFolders_StayInFirstChapter_AndShowPathInName()
    {
        Write("Книга/_research/_sections/Товары.md", "# Товары\n");

        var page = DocsScanner.Scan(_root, "Книга").Should().ContainSingle().Subject;

        page.Chapter.Should().Be("_research", "уровней у BookStack три, а у каталогов сколько угодно");
        page.Name.Should().Be("_sections/Товары",
            "иначе две одноимённые страницы из разных подкаталогов стали бы неразличимы");
    }

    [Fact]
    public void Scan_KeepsHeading_WhenNothingElseIsInTheFile()
    {
        Write("Книга/Заглушка.md", "# Пока пусто\n");

        var page = DocsScanner.Scan(_root, "Книга").Should().ContainSingle().Subject;

        page.Name.Should().Be("Пока пусто");
        page.Markdown.Should().NotBeNullOrWhiteSpace(
            "пустое тело портал не примет, и прогон свалился бы посреди выкладки");
    }

    [Fact]
    public void Scan_SkipsDotFolders()
    {
        Write("Книга/README.md", "# Оглавление\n");
        Write("Книга/.claude/skills/SKILL.md", "---\nname: skill\n---\n");

        DocsScanner.Scan(_root, "Книга").Should().ContainSingle()
            .Which.Name.Should().Be("Оглавление", "в точечных каталогах лежит оснастка, а не документация");
    }

    [Fact]
    public void Scan_NeverLeavesNameEmpty()
    {
        // Такое имя даёт пустой заголовок: ведущий номер снимается, а больше в имени ничего нет.
        Write("Книга/01-.md", "Текст без заголовка.\n");

        DocsScanner.Scan(_root, "Книга").Should().ContainSingle()
            .Which.Name.Should().NotBeNullOrWhiteSpace("пустое имя страницы портал не примет");
    }

    [Theory]
    [InlineData("Книга/Разделы/03-Расчёты.md", "Книга/ПланПоШагам/04-Расчёты.md")]
    [InlineData("Книга/Калькулятор.md", "Книга/_research/Калькулятор.md")]
    public void FileKey_SurvivesRenumberingAndMoving(string before, string after)
    {
        DocsScanner.FileKey(before).Should().Be(DocsScanner.FileKey(after),
            "по этой примете страница на портале узнаёт свой файл после переезда");
    }

    [Fact]
    public void FileKey_TellsDifferentFilesApart()
    {
        DocsScanner.FileKey("Книга/01-Старое.md").Should().NotBe(DocsScanner.FileKey("Книга/09-Новое.md"),
            "иначе удаление одного файла и добавление другого выглядело бы переименованием");
    }

    [Fact]
    public void Scan_OrdersByPath_AndKeepsRelativePathAsIdentity()
    {
        Write("Книга/02-Второй.md", "# Второй\n");
        Write("Книга/01-Первый.md", "# Первый\n");

        var pages = DocsScanner.Scan(_root, "Книга");

        pages.Select(p => p.Name).Should().Equal("Первый", "Второй");
        pages.Select(p => p.Priority).Should().Equal(0, 1);
        pages[0].RelPath.Should().Be("Книга/01-Первый.md",
            "путь файла это связь со страницей на портале, и он должен быть косыми чертами вперёд");
    }
}
