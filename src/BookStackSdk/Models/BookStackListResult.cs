using System.Globalization;

namespace BookStackSdk.Models;

/// <summary>
/// Ответ списочного маршрута: выданное окно записей и полное число доступных.
/// </summary>
/// <typeparam name="T">Тип записи списка.</typeparam>
/// <param name="Data">Записи выданного окна.</param>
/// <param name="Total">
/// Полное число доступных записей ПОСЛЕ применения фильтров, но без учёта окна. Считается сервером
/// отдельным запросом (<c>getCountForPagination()</c> в <c>app/Api/ListingResponseBuilder.php</c>),
/// поэтому по нему можно понять, есть ли что долистывать. <c>null</c> означает, что сервер поля не
/// прислал, и это не то же самое, что ноль.
/// </param>
/// <param name="RequestedOffset">
/// Смещение, которое запросили МЫ. Сервер его в ответе не повторяет, поле заполняется клиентом.
/// <c>null</c> означает, что смещение не задавалось и сервер взял своё умолчание (ноль).
/// </param>
/// <param name="RequestedCount">
/// Размер окна, который запросили МЫ, до того как сервер его подрезал. Сервер его в ответе тоже не
/// повторяет. Сколько записей выдано на самом деле, видно по длине <paramref name="Data"/>, и эти
/// два числа расходятся штатно: см. <see cref="BookStackListQuery.Count"/> про молчаливое
/// приведение к отрезку от 1 до 500.
/// </param>
public sealed record BookStackListResult<T>(
    IReadOnlyList<T> Data,
    int? Total,
    int? RequestedOffset,
    int? RequestedCount);

/// <summary>
/// Параметры списочного маршрута: окно, сортировка и фильтры.
/// </summary>
/// <remarks>
/// Все четыре списочных маршрута контента (<c>/api/pages</c>, <c>/api/books</c>,
/// <c>/api/chapters</c>, <c>/api/shelves</c>) обслуживаются одним и тем же кодом,
/// <c>app/Api/ListingResponseBuilder.php</c>, поэтому параметры у них общие. Разнится только
/// список полей, по которым разрешено сортировать и фильтровать, см. <see cref="BookStackSortFields"/>.
/// <para>
/// ВАЖНО, и это главная ловушка списков BookStack: НЕВЕРНОЕ ИМЯ ПОЛЯ НЕ ВЫЗЫВАЕТ ОШИБКИ. Замерено
/// на стенде 17.08.2026:
/// </para>
/// <list type="bullet">
/// <item><c>sort=bogus_field</c> отдаёт 200 и обычный порядок. В исходнике:
/// <c>if (!in_array($sortName, $this->fields)) { $sortName = $defaultSortName; }</c>, то есть
/// подставляется первое поле набора, а это всегда <c>id</c>;</item>
/// <item><c>sort=-revision_count</c> (поле существует у страницы, но сортировать по нему нельзя)
/// отдаёт записи в порядке УБЫВАНИЯ <c>id</c>: имя откинулось, а знак минуса остался в силе.
/// То есть ответ выглядит осмысленно отсортированным, просто не по тому полю, которое просили;</item>
/// <item><c>filter[bogus]=1</c> отдаёт полный список. Неизвестное поле фильтра просто выбрасывается
/// (<c>requestFilterToQueryFilter()</c> возвращает <c>null</c>, и такие условия отсеиваются);</item>
/// <item><c>filter[updated_at:bogus]</c> молча превращается в <c>filter[updated_at:eq]</c>:
/// неизвестный оператор заменяется на <c>eq</c>, и вместо «позже такой-то даты» получается «ровно
/// в такую-то секунду», то есть почти всегда пусто.</item>
/// </list>
/// <para>
/// Ни пометки о выброшенном, ни предупреждения в ответе нет, поэтому опечатка в имени поля
/// оборачивается не отказом, а тихо неверной выборкой. Именно ради этого заведены константы
/// <see cref="BookStackSortFields"/>: они сняты из исходников контроллеров и дают шанс поймать
/// опечатку на этапе сборки.
/// </para>
/// </remarks>
public sealed class BookStackListQuery
{
    /// <summary>Размер окна, параметр <c>count</c>.</summary>
    /// <remarks>
    /// Значение молча приводится к отрезку от 1 до 500: в исходнике
    /// <c>$count = max(min($maxCount, $count), 1)</c>, где <c>$maxCount</c> это
    /// <c>API_MAX_ITEM_COUNT</c> из <c>app/Config/api.php</c> со значением по умолчанию 500.
    /// Замер: <c>count=0</c> отдал одну запись, <c>count=-1</c> тоже одну. То есть просьба «не
    /// отдавать ничего» превращается в «отдать одну», и цикл, построенный на нулевом окне, никогда
    /// не закончится. Если поле не задано, сервер берёт <c>API_DEFAULT_ITEM_COUNT</c>, по умолчанию 100.
    /// </remarks>
    public int? Count { get; set; }

    /// <summary>Смещение от начала выборки, параметр <c>offset</c>.</summary>
    /// <remarks>
    /// Отрицательное значение приводится к нулю (<c>max(0, ...)</c>). Смещение за пределом выборки
    /// это не ошибка: замер с <c>offset=1000</c> на трёх записях дал <c>{"data":[],"total":3}</c>.
    /// </remarks>
    public int? Offset { get; set; }

    /// <summary>
    /// Поле сортировки, параметр <c>sort</c>. Имена брать из <see cref="BookStackSortFields"/>.
    /// </summary>
    /// <remarks>
    /// Умолчание сервера это <c>id</c> по возрастанию: у всех четырёх маршрутов <c>id</c> стоит
    /// первым в наборе полей, а именно первое поле подставляется, когда сортировка не задана или
    /// не опознана.
    /// </remarks>
    public string? SortBy { get; set; }

    /// <summary>Сортировать по убыванию (в параметре это ведущий минус).</summary>
    /// <remarks>
    /// Направление разбирается ОТДЕЛЬНО от имени поля и переживает откат неопознанного имени,
    /// см. замер про <c>sort=-revision_count</c> в описании класса. Без знака сортировка идёт
    /// по возрастанию.
    /// </remarks>
    public bool Descending { get; set; }

    /// <summary>Изменённые строго ПОЗЖЕ указанного момента (<c>filter[updated_at:gt]</c>).</summary>
    /// <remarks>
    /// Момент отправляется в UTC. Это не вкусовщина: BookStack хранит и отдаёт отметки времени в
    /// UTC (замер: запись, созданная в 16:55 по Владивостоку, вернулась как
    /// <c>2026-08-17T06:55:37.000000Z</c>), а фильтр сравнивает присланную строку с тем, что лежит
    /// в базе. Замер границы: <c>gt=2026-08-17T07:00:00Z</c> нашёл запись от 07:00:38Z, а
    /// <c>gt=2026-08-17T17:00:00Z</c> (тот же момент, записанный как местное время) не нашёл ничего.
    /// </remarks>
    public DateTimeOffset? UpdatedAfter { get; set; }

    /// <summary>Изменённые строго РАНЬШЕ указанного момента (<c>filter[updated_at:lt]</c>).</summary>
    public DateTimeOffset? UpdatedBefore { get; set; }

    /// <summary>Созданные строго позже указанного момента (<c>filter[created_at:gt]</c>).</summary>
    public DateTimeOffset? CreatedAfter { get; set; }

    /// <summary>Созданные строго раньше указанного момента (<c>filter[created_at:lt]</c>).</summary>
    public DateTimeOffset? CreatedBefore { get; set; }

    /// <summary>
    /// Остальные фильтры парами «поле» и «значение». Ключ либо имя поля, либо имя с оператором
    /// через двоеточие, например <c>book_id</c> или <c>name:like</c>.
    /// </summary>
    /// <remarks>
    /// Операторов ровно семь, они перечислены в <c>ListingResponseBuilder::$filterOperators</c>:
    /// <c>eq</c>, <c>ne</c>, <c>gt</c>, <c>lt</c>, <c>gte</c>, <c>lte</c>, <c>like</c>. Без
    /// двоеточия применяется <c>eq</c>. У <c>like</c> знаки подстановки надо ставить самому:
    /// замер <c>filter[name:like]=%markdown%</c> нашёл две страницы, а без процентов не нашёл ничего.
    /// <para>
    /// Поле, которого нет в наборе для этой сущности, выбрасывается молча, см. описание класса.
    /// Проверить, что имя допустимо, можно по <see cref="BookStackSortFields"/>: набор полей для
    /// фильтрации и для сортировки у контента один и тот же (контроллеры страниц, книг, глав и полок
    /// не вызывают <c>setFilterableFields()</c>, поэтому берётся тот же список, что и для выдачи).
    /// </para>
    /// </remarks>
    public Dictionary<string, string> Filters { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Копия параметров с другим окном. Нужна инкрементальному обходу: он двигает смещение, не
    /// трогая сортировку и фильтры вызывающего.
    /// </summary>
    public BookStackListQuery WithWindow(int offset, int count)
    {
        var copy = new BookStackListQuery
        {
            Count = count,
            Offset = offset,
            SortBy = SortBy,
            Descending = Descending,
            UpdatedAfter = UpdatedAfter,
            UpdatedBefore = UpdatedBefore,
            CreatedAfter = CreatedAfter,
            CreatedBefore = CreatedBefore,
        };

        foreach (var pair in Filters)
            copy.Filters[pair.Key] = pair.Value;

        return copy;
    }

    /// <summary>Собирает строку запроса вместе с ведущим знаком вопроса. Пустая, если задавать нечего.</summary>
    public string ToQueryString()
    {
        var parts = new List<string>();

        if (Count is { } count)
            parts.Add("count=" + count.ToString(CultureInfo.InvariantCulture));

        if (Offset is { } offset)
            parts.Add("offset=" + offset.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(SortBy))
            parts.Add("sort=" + Uri.EscapeDataString((Descending ? "-" : string.Empty) + SortBy.Trim()));

        AddFilter(parts, "updated_at:gt", UpdatedAfter);
        AddFilter(parts, "updated_at:lt", UpdatedBefore);
        AddFilter(parts, "created_at:gt", CreatedAfter);
        AddFilter(parts, "created_at:lt", CreatedBefore);

        foreach (var pair in Filters)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            parts.Add(
                Uri.EscapeDataString($"filter[{pair.Key.Trim()}]") + "=" + Uri.EscapeDataString(pair.Value ?? string.Empty));
        }

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    /// <summary>
    /// Добавляет фильтр по отметке времени.
    /// </summary>
    /// <remarks>
    /// Формат <c>yyyy-MM-ddTHH:mm:ssZ</c> выбран замером, а не вкусом. Проверены четыре записи
    /// одного и того же момента: голая дата <c>2026-08-17</c>, дата с временем через пробел,
    /// ISO с <c>Z</c> и ISO с микросекундами. Все четыре сервер принимает, но пробел и знак плюс
    /// в смещении часового пояса пришлось бы экранировать вручную (плюс в строке запроса читается
    /// как пробел), а форма с <c>Z</c> не содержит ни того, ни другого. Дробная часть отброшена
    /// намеренно: сравнение идёт по секундам, и лишние нули только удлиняют адрес.
    /// </remarks>
    private static void AddFilter(List<string> parts, string key, DateTimeOffset? value)
    {
        if (value is not { } moment)
            return;

        var text = moment.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        parts.Add(Uri.EscapeDataString($"filter[{key}]") + "=" + Uri.EscapeDataString(text));
    }
}

/// <summary>
/// Поля, по которым списочные маршруты РЕАЛЬНО умеют сортировать и фильтровать.
/// </summary>
/// <remarks>
/// Наборы сняты из исходников контроллеров в контейнере стенда 17.08.2026 (BookStack 26.05.3),
/// из вызовов <c>apiListingResponse()</c> в <c>app/Entities/Controllers/</c>. Один и тот же набор
/// работает и на сортировку, и на фильтрацию: контроллеры контента не сужают список вызовом
/// <c>setFilterableFields()</c>, а <c>ListingResponseBuilder</c> в этом случае берёт поля выдачи.
/// <para>
/// ВАЖНО: в ответе списка есть поля, которых в этих наборах НЕТ, и по ним ни сортировать, ни
/// фильтровать нельзя, хотя выглядят они равноправно. У страницы это <c>revision_count</c>,
/// <c>editor</c> и <c>book_slug</c> (они добавлены отдельным <c>addSelect</c> мимо набора), у книги
/// и полки это <c>cover</c> и <c>image_id</c>. Замер: <c>filter[editor]=markdown</c> вернул ВСЕ
/// страницы, включая те, у которых редактор другой.
/// </para>
/// </remarks>
public static class BookStackSortFields
{
    /// <summary>Общее умолчание всех четырёх маршрутов: <c>id</c> по возрастанию.</summary>
    public const string DefaultField = "id";

    /// <summary>Поля списка страниц (<c>GET /api/pages</c>).</summary>
    public static class Pages
    {
        public const string Id = "id";
        public const string BookId = "book_id";
        public const string ChapterId = "chapter_id";
        public const string Name = "name";
        public const string Slug = "slug";
        public const string Priority = "priority";
        public const string Draft = "draft";
        public const string Template = "template";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
        public const string CreatedBy = "created_by";
        public const string UpdatedBy = "updated_by";
        public const string OwnedBy = "owned_by";
    }

    /// <summary>Поля списка книг (<c>GET /api/books</c>).</summary>
    public static class Books
    {
        public const string Id = "id";
        public const string Name = "name";
        public const string Slug = "slug";
        public const string Description = "description";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
        public const string CreatedBy = "created_by";
        public const string UpdatedBy = "updated_by";
        public const string OwnedBy = "owned_by";
    }

    /// <summary>Поля списка глав (<c>GET /api/chapters</c>).</summary>
    public static class Chapters
    {
        public const string Id = "id";
        public const string BookId = "book_id";
        public const string Name = "name";
        public const string Slug = "slug";
        public const string Description = "description";
        public const string Priority = "priority";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
        public const string CreatedBy = "created_by";
        public const string UpdatedBy = "updated_by";
        public const string OwnedBy = "owned_by";
    }

    /// <summary>Поля списка полок (<c>GET /api/shelves</c>).</summary>
    public static class Shelves
    {
        public const string Id = "id";
        public const string Name = "name";
        public const string Slug = "slug";
        public const string Description = "description";
        public const string CreatedAt = "created_at";
        public const string UpdatedAt = "updated_at";
        public const string CreatedBy = "created_by";
        public const string UpdatedBy = "updated_by";
        public const string OwnedBy = "owned_by";
    }
}
