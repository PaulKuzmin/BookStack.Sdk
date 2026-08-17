using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BookStackSdk.Abstractions;
using BookStackSdk.DependencyInjection;
using BookStackSdk.Errors;
using BookStackSdk.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Общая оснастка стендовых проверок BookStack: собранный боевым способом клиент, проверка
/// живости стенда, замок от прода, отпечаток прогона и уборка за собой.
/// </summary>
/// <remarks>
/// Четыре вещи, ради которых фикстура существует.
/// <list type="number">
/// <item>
/// Клиент собирается НАСТОЯЩИМ расширением DI из поставляемой сборки. Собрать <c>HttpClient</c>
/// руками было бы проще, но тогда проверка проверяла бы нашу реконструкцию клиента: свою склейку
/// адреса, свой заголовок, свой порядок обработчиков. Ломается у пользователя SDK ровно это, а не
/// разбор JSON, и такая проверка о поломке промолчала бы.
/// </item>
/// <item>
/// Живость стенда проверяется ДО первой проверки и отдельно от токена. Заданная переменная это
/// обещание стенда, поэтому молчащий стенд тут не пропуск, а отказ с адресом и подсказкой.
/// </item>
/// <item>
/// Установка опознаётся <see cref="ProductionGuard"/> ДО того, как хоть одна проверка что-то
/// создаст. Проверка стоит именно в фикстуре, а не в теле каждого теста, потому что забыть её в
/// одном тесте из двадцати это вопрос времени, а цена промаха по адресу тут не «мусор на стенде».
/// </item>
/// <item>
/// Всё созданное попадает в журнал <c>stand-litter-{runId}.jsonl</c> СРАЗУ, до того как прогон
/// успеет упасть. Прибитый прогон не разбирает за собой, а удаление в BookStack мягкое: без
/// журнала оставшееся пришлось бы искать глазами в корзине.
/// </item>
/// </list>
/// <para>
/// Стенда нет вовсе: фикстура молча ничего не поднимает, а обращение к <see cref="Services"/>
/// падает с причиной. Разбирать этот случай должны <see cref="StandFactAttribute"/> (пропуск с
/// причиной) и <see cref="StrictModeGuardTests"/> (отказ в строгом режиме), а не фикстура: в
/// нестрогом прогоне на машине без докера её конструктор отрабатывает и на пропуски не влияет.
/// </para>
/// </remarks>
public sealed class StandFixture : IAsyncLifetime
{
    /// <summary>Ждём ответа стенда при проверке живости. Стенд локальный, долго ждать нечего.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Предел на уборку ОДНОЙ записи. Уборка страницы или книги это два запроса (удаление и
    /// добивание из корзины), поэтому предел заметно больше, чем у проверки живости.
    /// </summary>
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>UTF-8 без метки порядка байтов: с меткой первая строка журнала не разбирается как JSON.</summary>
    private static readonly UTF8Encoding JournalEncoding = new(encoderShouldEmitUTF8Identifier: false);

    private readonly Lock _sync = new();
    private readonly List<Tracked> _created = [];
    private ServiceProvider? _services;

    public StandFixture() =>
        LitterJournalPath = Path.Combine(AppContext.BaseDirectory, $"stand-litter-{RunId}.jsonl");

    /// <summary>
    /// Короткий отпечаток прогона. Идёт в имена всего, что проверки создают на стенде.
    /// </summary>
    /// <remarks>
    /// Без него соседние прогоны находили бы чужие записи и проверка «нашлось то, что создали»
    /// проходила бы, ничего не проверив. Для BookStack это вдвойне важно: поиск ищет по всему
    /// содержимому установки, а короткие имена (slug) освобождаются сразу после мягкого удаления
    /// и достаются следующему созданному с тем же названием.
    /// </remarks>
    public string RunId { get; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Полный путь журнала созданного на стенде.</summary>
    public string LitterJournalPath { get; }

    /// <summary>
    /// Что ответил <c>GET /api/system</c> при опознании установки. Заполняется в
    /// <see cref="InitializeAsync"/>.
    /// </summary>
    public BookStackSystemInfo? SystemInfo { get; private set; }

    /// <summary>
    /// Контейнер, собранный расширением DI поставляемой сборки.
    /// </summary>
    /// <exception cref="InvalidOperationException">Стенд не настроен, поднимать было нечего.</exception>
    public IServiceProvider Services => _services ?? throw new InvalidOperationException(
        "Стендовый клиент BookStack не собран: " + StandGate.Unavailable);

    /// <summary>Сервис SDK из контейнера, например <c>Api&lt;IBookStackContentApi&gt;()</c>.</summary>
    public T Api<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        if (!StandGate.IsConfigured)
            return;

        Journal("run-started", "stand", StandGate.Url!, StandGate.Describe());

        _services = BuildProvider();
        await EnsureReachableAsync().ConfigureAwait(false);
        SystemInfo = await EnsureStandAndTokenAsync().ConfigureAwait(false);

        Journal("stand-identified", "instance", SystemInfo.InstanceId ?? "(нет)", SystemInfo.BaseUrl);
    }

    /// <summary>
    /// Собирает контейнер ровно тем расширением, которое поставляется пользователям SDK.
    /// </summary>
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // ⚠️ Именно AddBookStack, а не new HttpClient с руками собранным заголовком. Иначе
        // стендовая проверка молчала бы о том, что ломается на самом деле: сборка заголовка из
        // двух половинок токена, склейка базового адреса с /api/, кодировка тела (без явной UTF-8
        // кириллица приезжает пустой), порядок обработчиков и разбор Retry-After. Всё это живёт в
        // расширении DI, и проверять надо его.
        services.AddBookStack(o =>
        {
            o.BaseUrl = StandGate.Url!;
            o.TokenId = StandGate.TokenId!;
            o.TokenSecret = StandGate.TokenSecret!;
        });

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Стенд вообще отвечает по своему адресу.
    /// </summary>
    /// <remarks>
    /// Проверка идёт ОТДЕЛЬНЫМ голым клиентом и до всякого токена, потому что это два разных
    /// отказа с разной починкой: «докер не поднят» чинится <c>docker compose up -d</c>, а «токен
    /// не принят» перевыпуском токена. Смешать их в одно сообщение значит отправить человека
    /// чинить не то.
    /// <para>
    /// Годится ЛЮБОЙ код ответа: страница входа, редирект, даже 500 означают, что по адресу
    /// кто-то живой. Проверяется транспорт, а не поведение.
    /// </para>
    /// </remarks>
    private static async Task EnsureReachableAsync()
    {
        using var probe = new HttpClient { Timeout = ProbeTimeout };

        try
        {
            using var response = await probe
                .GetAsync(StandGate.Url, HttpCompletionOption.ResponseHeadersRead)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is HttpRequestException or OperationCanceledException)
        {
            throw new InvalidOperationException(
                $"Стенд BookStack по адресу {StandGate.Url} не отвечает ({e.GetType().Name}: " +
                $"{e.Message}). Переменная {StandGate.UrlVariable} задана, значит стенд ОБЕЩАН, и " +
                "молча пропустить проверки тут нельзя: пропуск выглядел бы как «всё в порядке». " +
                StandGate.Hint,
                e);
        }
    }

    /// <summary>
    /// Токен принят, и по адресу именно стенд, а не боевой портал.
    /// </summary>
    /// <remarks>
    /// Оба вопроса решает один дешёвый <c>GET /api/system</c>: он не требует прав администратора,
    /// но требует принятого токена, и он же отдаёт <c>instance_id</c>, по которому установка
    /// опознаётся. Заодно это первая проверка того, что собранный DI клиент работоспособен.
    /// </remarks>
    private async Task<BookStackSystemInfo> EnsureStandAndTokenAsync()
    {
        using var cts = new CancellationTokenSource(ProbeTimeout * 2);

        BookStackSystemInfo? info;
        try
        {
            info = await Api<IBookStackSystemApi>().GetAsync(cts.Token).ConfigureAwait(false);
        }
        catch (BookStackApiException e)
        {
            throw new InvalidOperationException(
                $"Стенд BookStack по адресу {StandGate.Url} отвечает, но не принял токен из " +
                $"{StandGate.TokenVariable}: {e.Message}. Напоминание из замера: перестановка " +
                "половинок токена местами даёт ровно такой же 401, как отозванный или чужой " +
                "токен, порядок в переменной именно id:secret. " + StandGate.Hint,
                e);
        }

        if (info is null)
        {
            throw new InvalidOperationException(
                $"Стенд BookStack по адресу {StandGate.Url} ответил на GET /api/system пустым " +
                "телом. Опознать установку нечем, а без опознания пишущие проверки запускать " +
                "нельзя. " + StandGate.Hint);
        }

        // Замок от прода стоит ЗДЕСЬ, до первой созданной страницы, и он не пропуск, а отказ:
        // «не тот адрес» это не ненастроенная машина, а прямая опасность.
        ProductionGuard.EnsureWritable(info, StandGate.Url);

        return info;
    }

    // ---- Реестр созданного ----

    /// <summary>
    /// Записать созданное на стенде: в журнал сразу, в реестр на уборку.
    /// </summary>
    /// <param name="kind">Что создано: <c>page</c>, <c>book</c>, <c>image</c> и подобное.</param>
    /// <param name="id">Идентификатор на стенде, по которому это можно найти руками.</param>
    /// <param name="remove">Как это убрать. Вызовется в <see cref="DisposeAsync"/>.</param>
    /// <remarks>
    /// Способ уборки приходит извне намеренно: фикстура не должна знать, какими маршрутами
    /// пользуется конкретная проверка, а проверка не должна вспоминать про уборку в конце тела,
    /// куда до неё может не дойти управление.
    /// </remarks>
    public void Track(string kind, string id, Func<CancellationToken, Task> remove)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(remove);

        // Реестр раньше журнала: отказ записи в файл не должен отменять уборку уже созданного.
        lock (_sync)
            _created.Add(new Tracked(kind, id, remove));

        Journal("created", kind, id, null);
    }

    /// <inheritdoc cref="Track(string,string,Func{CancellationToken,Task})"/>
    public void Track(string kind, int id, Func<CancellationToken, Task> remove) =>
        Track(kind, id.ToString(CultureInfo.InvariantCulture), remove);

    /// <summary>Страница, созданная проверкой: удаление плюс добивание из корзины.</summary>
    public void TrackPage(int id) => TrackContent(
        BookStackEntityType.Page, id, (api, ct) => api.DeletePageAsync(id, ct));

    /// <summary>Глава, созданная проверкой: удаление плюс добивание из корзины.</summary>
    public void TrackChapter(int id) => TrackContent(
        BookStackEntityType.Chapter, id, (api, ct) => api.DeleteChapterAsync(id, ct));

    /// <summary>Книга, созданная проверкой: удаление плюс добивание из корзины.</summary>
    public void TrackBook(int id) => TrackContent(
        BookStackEntityType.Book, id, (api, ct) => api.DeleteBookAsync(id, ct));

    /// <summary>Полка, созданная проверкой: удаление плюс добивание из корзины.</summary>
    /// <remarks>Книги с полки не удаляются вместе с ней, их надо отмечать отдельно.</remarks>
    public void TrackShelf(int id) => TrackContent(
        BookStackEntityType.Bookshelf, id, (api, ct) => api.DeleteShelfAsync(id, ct));

    /// <summary>
    /// Учётка, созданная проверкой.
    /// </summary>
    /// <remarks>
    /// Удаление пользователя НЕ мягкое, в корзину он не едет, поэтому добивать нечего. Владение
    /// содержимым никому не передаётся: проверки убирают своё содержимое сами, а передача
    /// подсунула бы уборке чужие права.
    /// </remarks>
    public void TrackUser(int id) =>
        Track("user", id, ct => Api<IBookStackUsersApi>().DeleteAsync(id, null, ct));

    /// <summary>
    /// Уборка содержимого: мягкое удаление, а следом добивание записи из корзины.
    /// </summary>
    /// <remarks>
    /// ⚠️ Без второго шага уборки нет: замерено, что после <c>DELETE</c> объект пропадает из
    /// списков и чтения, но остаётся лежать в <c>/api/recycle-bin</c> и продолжает занимать место,
    /// а его содержимое видно всякому, у кого есть права на корзину. Прогон, который «прибрался»
    /// одним удалением, за неделю набивает корзину сотнями своих проб.
    /// </remarks>
    private void TrackContent(
        string entityType, int id, Func<IBookStackContentApi, CancellationToken, Task> delete)
    {
        Track(entityType, id, async ct =>
        {
            await delete(Api<IBookStackContentApi>(), ct).ConfigureAwait(false);
            await PurgeFromRecycleBinAsync(entityType, id, ct).ConfigureAwait(false);
        });
    }

    /// <summary>
    /// Находит в корзине запись об удалении конкретного объекта и добивает её окончательно.
    /// </summary>
    /// <param name="deletableType">Вид объекта: значения <see cref="BookStackEntityType"/>.</param>
    /// <param name="deletableId">Идентификатор самого объекта, а НЕ записи корзины.</param>
    /// <param name="ct">Отмена.</param>
    /// <returns>Запись нашлась и была добита.</returns>
    /// <remarks>
    /// ⚠️ Ищем по ПАРЕ (вид, идентификатор), а добиваем по идентификатору ЗАПИСИ КОРЗИНЫ. Это два
    /// разных числа, и перепутать их легко: у страницы 12 в корзине запись, скажем, 5, и
    /// <c>DELETE /api/recycle-bin/12</c> добил бы чужое удаление. Отменить это нельзя, поэтому
    /// поиск здесь, а не арифметика.
    /// <para>
    /// Искать в корзине по имени нечем (имя лежит внутри поддерева <c>deletable</c>), поэтому
    /// идём окнами. Предел в тысячу записей намеренный: если проба не нашлась в таком окне, она
    /// либо уже добита, либо корзина стенда захламлена настолько, что разбираться надо руками, а
    /// не молотить страницы в уборке.
    /// </para>
    /// </remarks>
    public async Task<bool> PurgeFromRecycleBinAsync(
        string deletableType, int deletableId, CancellationToken ct = default)
    {
        const int window = 100;
        const int limit = 1000;

        var bin = Api<IBookStackRecycleBinApi>();

        for (var offset = 0; offset < limit; offset += window)
        {
            var page = await bin.ListAsync(window, offset, null, ct).ConfigureAwait(false);

            foreach (var item in page.Data)
            {
                if (item.Id is { } deletionId
                    && item.DeletableId == deletableId
                    && string.Equals(item.DeletableType, deletableType, StringComparison.OrdinalIgnoreCase))
                {
                    await bin.DestroyAsync(deletionId, ct).ConfigureAwait(false);
                    return true;
                }
            }

            if (page.Data.Count < window)
                break;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task DisposeAsync()
    {
        Tracked[] litter;
        lock (_sync)
        {
            litter = [.. _created];
            _created.Clear();
        }

        // Обратный порядок: созданное последним удаляется первым. Страница уходит раньше своей
        // книги, книга раньше полки, содержимое раньше своего владельца.
        for (var i = litter.Length - 1; i >= 0; i--)
        {
            var item = litter[i];
            try
            {
                using var cts = new CancellationTokenSource(CleanupTimeout);
                await item.Remove(cts.Token).ConfigureAwait(false);
                Journal("removed", item.Kind, item.Id, null);
            }
            catch (Exception e)
            {
                // Уборка не предмет проверки: её отказ не должен маскировать результат теста,
                // поэтому исключение наружу не идёт. Но и молчать нельзя: строка "failed" в
                // журнале это и есть список того, что осталось лежать на стенде или в его корзине.
                Journal("failed", item.Kind, item.Id, $"{e.GetType().Name}: {e.Message}");
                Console.WriteLine(
                    $"Стенд BookStack: не удалось убрать {item.Kind} {item.Id}. " +
                    $"Осталось на {StandGate.Url}, проверьте и корзину. Журнал: {LitterJournalPath}");
            }
        }

        if (_services is not null)
            await _services.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Дописывает строку в журнал прогона.
    /// </summary>
    /// <remarks>
    /// Каждая строка это отдельное открытие и закрытие файла. Дороже, чем держать поток открытым,
    /// но зато прибитый прогон (а стендовые проверки прибивают именно так) оставляет журнал
    /// дописанным до последнего созданного, а не до последнего сброса буфера. Ради этого журнал и
    /// заведён.
    /// </remarks>
    private void Journal(string @event, string kind, string id, string? detail)
    {
        var line = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            ["run"] = RunId,
            ["event"] = @event,
            ["stand"] = StandGate.Url,
            ["kind"] = kind,
            ["id"] = id,
            ["detail"] = detail,
        });

        lock (_sync)
            File.AppendAllText(LitterJournalPath, line + Environment.NewLine, JournalEncoding);
    }

    private sealed record Tracked(string Kind, string Id, Func<CancellationToken, Task> Remove);
}
