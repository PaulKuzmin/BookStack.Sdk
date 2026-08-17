using BookStackSdk.Internal;
using BookStackSdk.Models;
using BookStackSdk.Search;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Поиск по содержимому (<c>GET /api/search</c>).
/// </summary>
/// <remarks>
/// Маршрут ровно один, и он ищет сразу по полкам, книгам, главам и страницам. Отдельного поиска
/// по виду сущности нет: сузить выдачу можно только фильтром внутри самой строки запроса, см.
/// <see cref="BookStackSearchQuery.OfTypes"/>.
/// <para>
/// Постраничность тут СВОЯ, не такая, как у списков: страница задаётся номером
/// (<c>page</c>, с единицы), а не смещением, и размер страницы ограничен сотней. Обычные списки
/// принимают <c>offset</c> и допускают до пятисот. Замерено: <c>count=101</c> даёт 422
/// «The count may not be greater than 100», <c>page=0</c> тоже 422. То есть здесь выход
/// за границы это ОШИБКА, а у списков молчаливое зажатие, и путать эти два поведения дорого.
/// </para>
/// </remarks>
public interface IBookStackSearchApi
{
    /// <summary>
    /// Ищет по готовой строке запроса.
    /// </summary>
    /// <param name="query">
    /// Строка в синтаксисе поиска BookStack. Если она собрана из пользовательского ввода, собирать
    /// её надо построителем <see cref="BookStackSearchQuery"/>: кавычка внутри чужого текста меняет
    /// смысл запроса, а не ломает его с ошибкой.
    /// </param>
    /// <param name="page">Номер страницы, с единицы. Ноль и меньше дают 422.</param>
    /// <param name="count">Размер страницы, не больше 100. Больше даёт 422.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// Пустая строка запроса это 422, а не «найти всё» (замерено): параметр <c>query</c> объявлен
    /// обязательным.
    /// </remarks>
    Task<BookStackPage<BookStackSearchResult>> SearchAsync(
        string query, int? page = null, int? count = null, CancellationToken ct = default);

    /// <summary>
    /// Ищет по собранному построителем запросу. Тот же вызов, что и по строке, просто без места,
    /// где строку можно склеить руками.
    /// </summary>
    Task<BookStackPage<BookStackSearchResult>> SearchAsync(
        BookStackSearchQuery query, int? page = null, int? count = null, CancellationToken ct = default);
}
