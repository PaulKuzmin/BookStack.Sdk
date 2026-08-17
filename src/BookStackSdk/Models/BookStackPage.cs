namespace BookStackSdk.Models;

/// <summary>
/// Страница BookStack: единица содержимого, лежащая в книге напрямую или внутри главы.
/// </summary>
/// <remarks>
/// Одна модель обслуживает и чтение одиночной страницы, и элемент списка, потому что маршруты
/// отдают ПОДМНОЖЕСТВА одного набора полей, а не разные сущности. Отсюда правило, общее для всех
/// моделей контента: все поля nullable, умолчаний нет. Замер на стенде 17.08.2026 показывает, зачем:
/// <list type="bullet">
/// <item><c>GET /api/pages/{id}</c> отдаёт <c>html</c>, <c>markdown</c>, <c>raw_html</c> и
/// <c>tags</c>;</item>
/// <item><c>GET /api/pages</c> не отдаёт ни одного из них, зато отдаёт <c>book_slug</c>;</item>
/// <item>страницы внутри главы (<c>pages</c> в ответе <c>GET /api/chapters/{id}</c>) приходят в
/// той же форме, что и элемент списка.</item>
/// </list>
/// То есть пустой <see cref="Markdown"/> значит «в этом ответе поля не было» ничуть не реже, чем
/// «у страницы нет markdown». Подставлять пустую строку вместо <c>null</c> нельзя: это ровно то
/// умолчание, из-за которого потом невозможно понять, стирать содержимое или дочитать его.
/// </remarks>
public sealed class BookStackPage
{
    /// <summary>Идентификатор страницы.</summary>
    public int? Id { get; set; }

    /// <summary>Вид сущности, <c>page</c>. Приходит при чтении, создании и обновлении, но не в списке.</summary>
    public string? Type { get; set; }

    /// <summary>Заголовок страницы.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Слаг страницы, часть адреса.
    /// </summary>
    /// <remarks>
    /// ВАЖНО: слаг ПЕРЕСЧИТЫВАЕТСЯ при переименовании. Замер: страница со слагом
    /// <c>stranica-iz-markdown-MIU</c> после <c>PUT</c> с одним только новым именем получила слаг
    /// <c>stranica-iz-markdown-pravka-1</c>. В исходнике <c>app/Entities/Repos/BaseRepo.php</c>:
    /// <c>if ($entity-&gt;isDirty('name') || empty($entity-&gt;slug)) { $this-&gt;refreshSlug($entity); }</c>.
    /// Старый адрес BookStack запоминает в истории слагов и умеет по нему перенаправлять, но любая
    /// ссылка, собранная вручную из слага, после переименования указывает не туда. Единственный
    /// устойчивый ключ страницы это <see cref="Id"/>.
    /// </remarks>
    public string? Slug { get; set; }

    /// <summary>Книга, которой принадлежит страница.</summary>
    public int? BookId { get; set; }

    /// <summary>
    /// Слаг книги. Приходит только в списках и во вложенных наборах, при чтении одиночной страницы
    /// его нет.
    /// </summary>
    public string? BookSlug { get; set; }

    /// <summary>Глава, если страница лежит в главе. <c>null</c>, если страница лежит в книге напрямую.</summary>
    public int? ChapterId { get; set; }

    /// <summary>
    /// Позиция среди соседей. Ею задаётся порядок показа внутри книги или главы.
    /// </summary>
    /// <remarks>
    /// При создании без явного значения сервер назначает его сам, продолжая нумерацию соседей
    /// (замер: первая страница в пустой главе получила <c>priority: 1</c>, страницы в книге получили
    /// 6 и 7 вслед за уже лежавшими там).
    /// </remarks>
    public int? Priority { get; set; }

    /// <summary>Когда страница создана. UTC.</summary>
    public DateTimeOffset? CreatedAt { get; set; }

    /// <summary>Когда страница изменена. UTC.</summary>
    /// <remarks>
    /// Это поле и есть точка опоры инкрементального обхода: по нему работает фильтр
    /// <see cref="BookStackListQuery.UpdatedAfter"/>. Помнить, что смена одних только тегов тоже
    /// двигает отметку: в исходнике после записи тегов стоит явный <c>$entity-&gt;touch()</c>.
    /// </remarks>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Кто создал.
    /// </summary>
    /// <remarks>
    /// Форма этого поля двоякая, и на маршрутах контента она распределена так (замер на стенде
    /// 17.08.2026, одни и те же сущности):
    /// <list type="bullet">
    /// <item>чтение одиночной сущности (<c>GET /api/pages/{id}</c>, <c>/api/books/{id}</c>,
    /// <c>/api/chapters/{id}</c>, <c>/api/shelves/{id}</c>) отдаёт ОБЪЕКТ
    /// <c>{"id":1,"name":"Admin","slug":"admin"}</c>;</item>
    /// <item>любой список отдаёт ЧИСЛО;</item>
    /// <item>создание и обновление СТРАНИЦЫ отдают объект, а создание и обновление книги, главы и
    /// полки отдают число. То есть форма зависит не от сущности и не от действия по отдельности,
    /// а от их сочетания;</item>
    /// <item>вложенные наборы (<c>pages</c> внутри главы, <c>books</c> внутри полки) отдают число.</item>
    /// </list>
    /// Обе формы читает <see cref="BookStackUserRef"/> своим преобразователем. При числовой форме
    /// заполнен только идентификатор, имя и слаг остаются <c>null</c>, и это честно: сервер их не
    /// присылал, а не прислал пустыми.
    /// </remarks>
    public BookStackUserRef? CreatedBy { get; set; }

    /// <summary>Кто изменил последним.</summary>
    public BookStackUserRef? UpdatedBy { get; set; }

    /// <summary>Кто владеет страницей. От владельца считаются права на неё.</summary>
    public BookStackUserRef? OwnedBy { get; set; }

    /// <summary>
    /// Ключ вспомогательной записи страницы. Всегда совпадает с <see cref="Id"/>.
    /// </summary>
    /// <remarks>
    /// Поле не лишнее и не ошибка ответа: BookStack держит содержимое страницы в отдельной таблице
    /// <c>entity_page_data</c> (модель <c>app/Entities/Models/EntityPageData.php</c>, первичный ключ
    /// <c>page_id</c>), и при выборке этот ключ приезжает вместе с <c>draft</c>, <c>template</c>,
    /// <c>revision_count</c>, <c>editor</c>, <c>html</c> и <c>markdown</c>. Опираться на него
    /// незачем, но и выбрасывать из модели не за что: он приходит в каждом ответе про страницу.
    /// </remarks>
    public int? PageId { get; set; }

    /// <summary>Черновик ли страница. Черновики не видны в книге до публикации.</summary>
    public bool? Draft { get; set; }

    /// <summary>Шаблон ли страница. Шаблоны подставляются в новые страницы книги или главы.</summary>
    public bool? Template { get; set; }

    /// <summary>Сколько раз страницу правили.</summary>
    /// <remarks>
    /// ВАЖНО: сортировать и фильтровать по этому полю нельзя, хотя в ответе списка оно есть.
    /// Оно добавлено отдельным <c>addSelect</c> мимо набора полей выдачи, и попытка сортировки по
    /// нему молча откатывается к <c>id</c>, см. <see cref="BookStackSortFields"/>.
    /// </remarks>
    public int? RevisionCount { get; set; }

    /// <summary>
    /// Каким редактором страницу правили последний раз: <c>markdown</c>, <c>wysiwyg</c> и подобное.
    /// </summary>
    /// <remarks>
    /// Значение переключается САМО, по тому, чем прислали содержимое. Замер: страница, созданная с
    /// <c>markdown</c>, имела <c>editor: markdown</c>; после <c>PUT</c> с одним только <c>html</c>
    /// она стала <c>editor: wysiwyg</c>; после следующего <c>PUT</c> с <c>markdown</c> вернулась в
    /// <c>markdown</c>. Подробности и цена этого переключения в описании
    /// <see cref="BookStackPageUpdate.Markdown"/>.
    /// </remarks>
    public string? Editor { get; set; }

    /// <summary>
    /// Готовый HTML страницы: то, что BookStack показывает при просмотре.
    /// </summary>
    /// <remarks>
    /// Отличается от <see cref="RawHtml"/> тем, что здесь уже раскрыты вставки других страниц и
    /// выполнено экранирование. В исходнике <c>Page::forJsonDisplay()</c>:
    /// <c>setAttribute('html', (new PageContent($refreshed))-&gt;render())</c>. Приходит только при
    /// чтении, создании и обновлении, в списках его нет.
    /// </remarks>
    public string? Html { get; set; }

    /// <summary>
    /// Исходный markdown страницы.
    /// </summary>
    /// <remarks>
    /// Заполнен, только если страницу последний раз правили markdown-редактором. Пустая строка тут
    /// означает не «страница пуста», а «источником был HTML»: <c>PageContent::setNewHTML()</c>
    /// прямо ставит <c>$this-&gt;page-&gt;markdown = ''</c>. Замерено.
    /// </remarks>
    public string? Markdown { get; set; }

    /// <summary>
    /// HTML, как он лежит в базе: то, что BookStack показывает в редакторе.
    /// </summary>
    /// <remarks>
    /// Вставки других страниц здесь НЕ раскрыты, в отличие от <see cref="Html"/>. Для сравнения
    /// двух состояний страницы годится именно это поле: <see cref="Html"/> меняется вслед за
    /// чужими страницами, на которые ссылается наша.
    /// </remarks>
    public string? RawHtml { get; set; }

    /// <summary>
    /// Теги страницы. Приходят при чтении, создании и обновлении, в списках их нет.
    /// </summary>
    /// <remarks>
    /// Пустой список и <c>null</c> тут значат разное: пустой это «тегов нет», <c>null</c> это
    /// «маршрут теги не отдавал», и по списку страниц узнать теги нельзя вовсе. Про запись тегов
    /// см. <see cref="BookStackTag"/>.
    /// </remarks>
    public List<BookStackTag>? Tags { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"страница #{Id} {Name}";
}
