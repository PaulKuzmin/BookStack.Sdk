using BookStackSdk.Internal;
using BookStackSdk.Models;

namespace BookStackSdk.Abstractions;

/// <summary>
/// Корзина (<c>/api/recycle-bin</c>).
/// </summary>
/// <remarks>
/// Зачем это нужно вообще: удаление полки, книги, главы и страницы в BookStack МЯГКОЕ. Ответ
/// на <c>DELETE</c> приходит 204, объект пропадает из списков и из чтения (замерено: 404 после
/// удаления), но физически он остаётся и лежит здесь, пока его не добьют. То есть без этого
/// интерфейса удаление не доведено до конца.
/// <para>
/// А вот распространённое ожидание «пока не добьёшь, короткое имя занято» замером НЕ
/// подтвердилось, и это важно знать заранее. Проверено 17.08.2026: после удаления книги
/// с коротким именем <c>proba-sdk-zagruzok</c> новая книга с тем же названием получила ровно
/// то же самое короткое имя. Исходник объясняет почему: <c>SlugGenerator::slugInUse</c> ищет
/// занятое имя обычным запросом, а он удалённых не видит.
/// </para>
/// <para>
/// Из этого следует настоящая ловушка, которую надо держать в голове при восстановлении.
/// Замерено там же: удалили книгу, создали новую с тем же названием (она заняла освободившееся
/// короткое имя), восстановили старую из корзины, и в системе оказались ДВЕ книги с одинаковым
/// коротким именем. Восстановление не проверяет, свободно ли имя, и не переименовывает.
/// </para>
/// <para>
/// Права нужны двойные: и управление настройками, и управление правами. Одного мало.
/// </para>
/// </remarks>
public interface IBookStackRecycleBinApi
{
    /// <summary>
    /// Список записей корзины (<c>GET /api/recycle-bin</c>).
    /// </summary>
    /// <param name="count">Сколько вернуть.</param>
    /// <param name="offset">Сколько пропустить.</param>
    /// <param name="sort">Поле сортировки.</param>
    /// <param name="ct">Отмена.</param>
    /// <remarks>
    /// Список верхнеуровневый: удалённая книга это ОДНА запись, её главы и страницы отдельными
    /// записями не показываются, хотя удалены вместе с ней. Искать в корзине по имени нечем:
    /// имя лежит внутри поддерева <see cref="BookStackRecycleBinItem.Deletable"/>, а фильтровать
    /// можно только по полям самой записи.
    /// </remarks>
    Task<BookStackPage<BookStackRecycleBinItem>> ListAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        CancellationToken ct = default);

    /// <summary>
    /// Возвращает удалённое из корзины (<c>PUT /api/recycle-bin/{deletionId}</c>).
    /// </summary>
    /// <param name="deletionId">
    /// Идентификатор ЗАПИСИ КОРЗИНЫ (<see cref="BookStackRecycleBinItem.Id"/>), а не удалённого
    /// объекта.
    /// </param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Сколько сущностей вернулось, вместе с детьми.</returns>
    /// <remarks>
    /// Про столкновение коротких имён при восстановлении смотрите примечание к интерфейсу: это
    /// не теория, а замер.
    /// </remarks>
    Task<BookStackRecycleBinRestoreResult?> RestoreAsync(int deletionId, CancellationToken ct = default);

    /// <summary>
    /// Стирает удалённое окончательно (<c>DELETE /api/recycle-bin/{deletionId}</c>).
    /// </summary>
    /// <param name="deletionId">
    /// Идентификатор ЗАПИСИ КОРЗИНЫ, а не удалённого объекта. Перепутать легко, а отменить
    /// нельзя: после этого вызова объекта нет нигде.
    /// </param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Сколько сущностей стёрто, вместе с детьми (замерено: книга с одной страницей даёт 2).</returns>
    Task<BookStackRecycleBinDestroyResult?> DestroyAsync(int deletionId, CancellationToken ct = default);
}
