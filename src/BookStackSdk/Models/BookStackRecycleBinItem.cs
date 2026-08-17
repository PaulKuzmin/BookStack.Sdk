using System.Text.Json;

namespace BookStackSdk.Models;

/// <summary>
/// Запись корзины (<c>GET /api/recycle-bin</c>): одно удаление одного объекта верхнего уровня.
/// </summary>
/// <remarks>
/// ВАЖНО: <see cref="Id"/> это идентификатор ЗАПИСИ УДАЛЕНИЯ, а не удалённого объекта. Добивать
/// и восстанавливать надо по нему, а не по <see cref="DeletableId"/>. Перепутать легко, и цена
/// ошибки несимметрична: добивание необратимо.
/// <para>
/// Записей на один объект бывает меньше, чем удалённых сущностей: при удалении книги в корзину
/// уезжает ОДНА запись про книгу, а её главы и страницы уходят вместе с ней. Это видно по числам
/// в ответах: замер 17.08.2026 на книге с одной страницей дал <c>{"restore_count": 2}</c>
/// и <c>{"delete_count": 2}</c> на одну запись корзины.
/// </para>
/// </remarks>
public sealed class BookStackRecycleBinItem
{
    /// <summary>Идентификатор записи удаления. Его ждут восстановление и добивание.</summary>
    public int? Id { get; set; }

    /// <summary>Кто удалил.</summary>
    public int? DeletedBy { get; set; }

    /// <summary>Когда запись появилась в корзине, то есть когда объект удалили.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда запись менялась.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Вид удалённого объекта: <c>page</c>, <c>chapter</c>, <c>book</c> или <c>bookshelf</c>
    /// (значения те же, что у <see cref="BookStackEntityType"/>).
    /// </summary>
    public string? DeletableType { get; set; }

    /// <summary>Идентификатор удалённого объекта в пределах его вида.</summary>
    public int? DeletableId { get; set; }

    /// <summary>
    /// Сам удалённый объект, как его отдал сервер.
    /// </summary>
    /// <remarks>
    /// Намеренно оставлен неразобранным поддеревом JSON. Причина: форма зависит от вида и содержит
    /// поля, которых нет в обычных ответах. Замерено 17.08.2026: у страницы приходят
    /// <c>revision_count</c>, <c>editor</c>, <c>book_slug</c>, <c>page_id</c> и вложенный
    /// <c>parent</c> с книгой целиком; у книги приходят <c>pages_count</c> и <c>chapters_count</c>,
    /// которых обычное чтение книги не отдаёт. Заводить под это отдельную модель значило бы
    /// заводить ЕЩЁ ОДНУ форму книги и страницы, отличную и от списка, и от чтения, и обещать, что
    /// она полна. Разбирать это поддерево при необходимости должен вызывающий, зная
    /// <see cref="DeletableType"/>.
    /// <para>
    /// Для решения «что добивать» и «что восстанавливать» хватает <see cref="DeletableType"/>
    /// и <see cref="DeletableId"/>, они разобраны честными полями.
    /// </para>
    /// </remarks>
    public JsonElement? Deletable { get; set; }
}

/// <summary>Ответ восстановления (<c>PUT /api/recycle-bin/{id}</c>).</summary>
public sealed class BookStackRecycleBinRestoreResult
{
    /// <summary>
    /// Сколько сущностей вернулось. Считаются и дети: восстановление книги с одной страницей даёт 2
    /// (замерено).
    /// </summary>
    public int? RestoreCount { get; set; }
}

/// <summary>Ответ окончательного удаления (<c>DELETE /api/recycle-bin/{id}</c>).</summary>
public sealed class BookStackRecycleBinDestroyResult
{
    /// <summary>
    /// Сколько сущностей стёрто. Считаются и дети: добивание книги с одной страницей даёт 2
    /// (замерено).
    /// </summary>
    public int? DeleteCount { get; set; }
}
