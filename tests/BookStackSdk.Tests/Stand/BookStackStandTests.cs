using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BookStackSdk.Abstractions;
using BookStackSdk.Models;
using FluentAssertions;
using Xunit;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Проверки против ЖИВОГО BookStack из докерного стенда.
/// </summary>
/// <remarks>
/// Сюда попадает только то, где подставной транспорт доказывает наше намерение, но не согласие
/// сервера. Что мы отправили тело в UTF-8, докажет и заглушка; что оно ТАК ЖЕ И СОХРАНИЛОСЬ,
/// заглушка сочинить не может, а именно на этом ломалась питоновская обёртка: имя книги приезжало
/// пустым, и по коду ответа это было неотличимо от «вы забыли поле».
/// <para>
/// Ничего не читаем из того, чего сами не создали: стенд общий, на нём живут ручные пробы и чужие
/// прогоны. Всё созданное несёт отпечаток прогона и убирается фикстурой.
/// </para>
/// </remarks>
[Collection(nameof(StandCollection))]
public sealed class BookStackStandTests
{
    private readonly StandFixture _stand;

    public BookStackStandTests(StandFixture stand) => _stand = stand;

    private IBookStackContentApi Content => _stand.Api<IBookStackContentApi>();

    private string Name(string what) => $"sdk-{_stand.RunId}-{what}";

    // ------------------------------------------------------------------

    /// <summary>
    /// ⚠️ ГЛАВНЫЙ ЗАМОК: кириллица доезжает и возвращается посимвольно целой.
    /// </summary>
    /// <remarks>
    /// Ради этой проверки живой стенд и нужен. Порча кодировки не выглядит как ошибка разбора: она
    /// маскируется под «поле обязательно» либо просто даёт пустое имя при коде 200. То есть по
    /// ответу отличить испорченные байты от собственной опечатки нельзя, а тест на заглушке
    /// подтвердит лишь то, что мы отправили правильно.
    /// </remarks>
    [StandFact(DisplayName = "Стенд: кириллица сохраняется посимвольно")]
    public async Task Cyrillic_survives_the_round_trip()
    {
        var name = Name("книга ЖЁЛТЫЙ ёлка щи");
        var description = "Описание с ё, й, щ, ъ и «кавычками-ёлочками».";

        var book = await Content.CreateBookAsync(new BookStackBookCreate
        {
            Name = name,
            Description = description,
        });

        book.Should().NotBeNull();
        _stand.TrackBook(book!.Id!.Value);

        var reread = await Content.GetBookAsync(book.Id!.Value);

        reread.Should().NotBeNull();
        reread!.Name.Should().Be(name,
            "имя обязано вернуться посимвольно целым; пустое или урезанное имя здесь означает " +
            "испорченную кодировку тела, а не нашу опечатку");
        reread.Description.Should().Contain("ё",
            "буква ё выпадает первой при неверной кодировке и потому взята пробой");
    }

    /// <summary>
    /// ⚠️ Обложка действительно встаёт при отправке с подменой метода.
    /// </summary>
    /// <remarks>
    /// Проверяется не то, что мы отправили, а то, что сервер это принял и сохранил. Важная
    /// оговорка: на ЭТОМ стенде честная отправка методом PUT тоже работает, потому что начиная с
    /// PHP 8.4 многочастное тело при PUT разбирается штатно. На PHP старше файл пропадает молча
    /// при коде 200. Поэтому SDK всегда шлёт подмену метода, а живой стенд подтверждает лишь то,
    /// что этот путь рабочий, а не то, что альтернативный сломан.
    /// </remarks>
    [StandFact(DisplayName = "Стенд: обложка книги встаёт через подмену метода")]
    public async Task Book_cover_is_actually_set()
    {
        var book = await Content.CreateBookAsync(new BookStackBookCreate { Name = Name("обложка") });
        _stand.TrackBook(book!.Id!.Value);

        var image = await _stand.Api<IBookStackUploadsApi>()
            .SetBookCoverAsync(book.Id!.Value, "cover.png", OnePixelPng(), "image/png");

        image.Should().NotBeNull("сервер обязан вернуть модель картинки, а не пустой ответ");

        var reread = await Content.GetBookAsync(book.Id!.Value);
        reread!.Cover.Should().NotBeNull(
            "обложка обязана быть видна при последующем чтении книги: ответ 200 сам по себе " +
            "ничего не доказывает, файл может пропасть молча");
    }

    /// <summary>
    /// ⚠️ Удаление мягкое, и наш токен пускает в корзину.
    /// </summary>
    /// <remarks>
    /// Это неизвестное, на котором висела вся уборка за собой. Удаление уводит объект в корзину, а
    /// не стирает: короткое имя остаётся занятым, и повторное создание с тем же именем поведёт
    /// себя иначе, чем первое. Добивание требует прав администратора у токена, и если стенд однажды
    /// начнут гонять под урезанной ролью, уборка сломается МОЛЧА. Поэтому право проверяется
    /// отдельно и прямо.
    /// </remarks>
    [StandFact(DisplayName = "Стенд: удаление мягкое, корзина доступна нашему токену")]
    public async Task Delete_is_soft_and_recycle_bin_is_reachable()
    {
        var book = await Content.CreateBookAsync(new BookStackBookCreate { Name = Name("корзина") });
        var id = book!.Id!.Value;

        await Content.DeleteBookAsync(id);

        var bin = await _stand.Api<IBookStackRecycleBinApi>().ListAsync(count: 100, sort: "-id");

        // Ищем по идентификатору удалённой сущности, а не по её имени: состав вложенного объекта
        // зависит от типа и приходит нетипизированным, а идентификатор мы только что создали сами.
        var mine = bin.Data.FirstOrDefault(i => i.DeletableId == id);

        mine.Should().NotBeNull(
            "удалённая книга обязана оказаться в корзине: удаление здесь мягкое, и без добивания " +
            "стенд копит мусор, а короткие имена остаются занятыми");

        var destroyed = await _stand.Api<IBookStackRecycleBinApi>().DestroyAsync(mine!.Id!.Value);
        destroyed.Should().NotBeNull(
            "добивание требует прав администратора у токена; если их нет, уборка ломается молча");

        var gone = await Content.ListBooksAsync(new BookStackListQuery { Count = 500 });
        gone.Data.Should().NotContain(b => b.Id == id, "после добивания книги быть не должно");
    }

    /// <summary>
    /// Список пользователей не отдаёт роли, их приходится дотягивать.
    /// </summary>
    /// <remarks>
    /// Утверждение о чужом ответе, поэтому проверяется на чужом ответе. От него зависит, делает ли
    /// вызывающий один запрос или N плюс один, а это разница между «списком» и «обходом».
    /// </remarks>
    [StandFact(DisplayName = "Стенд: роли в списке пользователей не приходят")]
    public async Task User_list_does_not_carry_roles()
    {
        var users = await _stand.Api<IBookStackUsersApi>()
            .ListAsync(new BookStackUserListQuery { Count = 5 });

        users.Data.Should().NotBeEmpty("на стенде есть хотя бы администратор");
        users.Data.Should().OnlyContain(u => u.Roles == null || u.Roles.Count == 0,
            "роли в списке не приходят, и рассчитывать на них нельзя: их дотягивают по каждому");

        var one = await _stand.Api<IBookStackUsersApi>().GetAsync(users.Data[0].Id!.Value);
        one.Should().NotBeNull();
        one!.Roles.Should().NotBeNullOrEmpty("а в карточке роли уже есть");
    }

    /// <summary>
    /// Листание работает на настоящих числах, а не на выдуманных.
    /// </summary>
    /// <remarks>
    /// Абсолютных количеств здесь нет намеренно: стенд общий, рядом идут ручные пробы и чужие
    /// прогоны. Проверяется поведение постранички, а не содержимое стенда.
    /// </remarks>
    [StandFact(DisplayName = "Стенд: постраничное чтение согласовано с общим числом")]
    public async Task Paging_agrees_with_the_reported_total()
    {
        var first = await Content.ListBooksAsync(new BookStackListQuery { Count = 1, Offset = 0 });

        first.Total.Should().NotBeNull("сервер сообщает общее число, и оно нужно для обхода");
        first.Data.Count.Should().BeLessThanOrEqualTo(1, "просили одну запись, больше приходить не должно");

        if (first.Total > 1)
        {
            var second = await Content.ListBooksAsync(new BookStackListQuery { Count = 1, Offset = 1 });
            second.Data.Should().NotBeEmpty();
            second.Data[0].Id.Should().NotBe(first.Data[0].Id,
                "смещение обязано двигать окно, иначе обход списка зациклится");
        }
    }

    /// <summary>
    /// ⚠️ Установка опознана как стенд, а не как боевая.
    /// </summary>
    /// <remarks>
    /// Замок против прогона по проду. Между стендом и боевым порталом разница ровно в одной строке
    /// адреса, и пишущие проверки на боевом создали бы мусор в живой документации. Идентификатор
    /// установки уникален, поэтому подмена одной переменной окружения увести прогон не сможет.
    /// </remarks>
    [StandFact(DisplayName = "Стенд: установка опознана и разрешена к записи")]
    public async Task Installation_is_recognised_as_a_stand()
    {
        var info = await _stand.Api<IBookStackSystemApi>().GetAsync();

        info.Should().NotBeNull();
        info!.InstanceId.Should().NotBeNullOrWhiteSpace(
            "идентификатор установки уникален и служит замком от прогона по боевому порталу");

        ProductionGuard.Refusal(info, StandGate.Url).Should().BeNull(
            "если правило отказывает, значит прогон целится не в стенд, и писать сюда нельзя");
    }

    /// <summary>
    /// Отпечаток прогона: созданное нами действительно лежит на сервере.
    /// </summary>
    /// <remarks>
    /// Ловит прогон, который технически состоялся, но по чужим данным, по кэшу или по случайно
    /// оставшейся заглушке. Сверяется не «нашлось что-то», а «нашлось именно наше».
    /// </remarks>
    [StandFact(DisplayName = "Стенд: созданное этим прогоном находится поиском")]
    public async Task What_this_run_created_is_findable_on_the_server()
    {
        var book = await Content.CreateBookAsync(new BookStackBookCreate { Name = Name("отпечаток") });
        _stand.TrackBook(book!.Id!.Value);

        var page = await Content.CreatePageAsync(new BookStackPageCreate
        {
            BookId = book.Id,
            Name = Name("страница"),
            Markdown = $"# Отпечаток {_stand.RunId}\n\nСтраница создана стендовым прогоном SDK.",
        });

        page.Should().NotBeNull();
        _stand.TrackPage(page!.Id!.Value);

        var reread = await Content.GetPageAsync(page.Id!.Value);
        reread.Should().NotBeNull("страница обязана читаться с сервера, а не из нашей памяти");
        reread!.Name.Should().Contain(_stand.RunId,
            "отпечаток обязан быть в том, что вернул сервер: так исключается прогон по чужим данным");
    }

    // ------------------------------------------------------------------

    /// <summary>Однопиксельный PNG: минимальный настоящий файл для проверки загрузки.</summary>
    private static byte[] OnePixelPng() => Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");
}
