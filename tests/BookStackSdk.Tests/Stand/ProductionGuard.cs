using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BookStackSdk.Abstractions;
using BookStackSdk.Models;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Замок от прогона пишущих проверок по БОЕВОМУ BookStack.
/// </summary>
/// <remarks>
/// ⚠️ Зачем это заведено отдельно от гейта. Между стендом и боевым порталом разница ровно в одной
/// строке: <c>ALTWAY_TESTS_BOOKSTACK_URL=http://localhost:6875</c> против
/// <c>https://help.altway.pro</c>. Переменная задаётся руками, копируется из чужой шпаргалки,
/// остаётся в окружении сборочного агента с прошлого раза. Пишущая проверка создаёт и удаляет
/// содержимое, а удаление в BookStack мягкое: чтобы «прибраться», её уборка ещё и добивает
/// удалённое из корзины. Промах по адресу означает не «мусор на стенде», а стёртые страницы
/// у людей, и заметят это не в отчёте прогона.
/// <para>
/// Опознание идёт по <c>instance_id</c> из <c>GET /api/system</c>, потому что это единственное
/// поле, уникальное на установку (замер стенда 17.08.2026:
/// <c>261145ec-d28e-44fa-ad08-7cb29e279405</c>). Ни имя, ни версия для этого не годятся: они
/// совпадают у прода и любой его копии. Адрес тоже не годится сам по себе, он и есть то, что
/// путают, но он проверяется вторым слоем: копия прода, поднятая локально, унесла бы с собой
/// боевой <c>instance_id</c>, а боевой портал, доступный по служебному адресу, унёс бы боевой
/// <c>base_url</c>.
/// </para>
/// <para>
/// Правило устроено как БЕЛЫЙ список, а не как чёрный: неизвестная установка отвергается. Чёрный
/// список защищал бы только от уже перечисленных адресов, то есть от ошибок, которые кто-то уже
/// совершил. Список запрещённых хостов при этом тоже есть, но он не замена белому списку, а
/// отдельная растяжка на случай, если кто-то расширит белый список неаккуратно.
/// </para>
/// </remarks>
public static class ProductionGuard
{
    /// <summary>
    /// Переменная, которой можно назвать ЕЩЁ ОДИН стенд: его <c>instance_id</c> целиком.
    /// </summary>
    /// <remarks>
    /// Нужна для стенда на другой машине или в облаке сборки. Значение обязано быть точным
    /// идентификатором установки: никаких масок, никаких «разрешить всё». Задавая её, человек
    /// совершает отдельное осознанное действие и берёт ответственность на себя; ошибиться в этом
    /// значении случайно, копируя адрес, невозможно. Хосты из <see cref="ForbiddenHosts"/> она не
    /// разблокирует.
    /// </remarks>
    public const string InstanceIdVariable = "ALTWAY_TESTS_BOOKSTACK_INSTANCE_ID";

    /// <summary>Установки, которые считаются стендами. Замер 17.08.2026.</summary>
    public static IReadOnlyList<string> KnownStandInstanceIds { get; } =
    [
        "261145ec-d28e-44fa-ad08-7cb29e279405",
    ];

    /// <summary>Хосты, на которых стенд бывает. Второй слой проверки, поверх идентификатора.</summary>
    public static IReadOnlyList<string> KnownStandHosts { get; } =
    [
        "localhost",
        "127.0.0.1",
        "::1",
    ];

    /// <summary>
    /// Хосты, куда пишущим проверкам нельзя никогда, чем бы их ни разрешали.
    /// </summary>
    /// <remarks>
    /// Список пополняется по мере появления боевых адресов. Он не заменяет белый список, а
    /// страхует от неаккуратного его расширения.
    /// </remarks>
    public static IReadOnlyList<string> ForbiddenHosts { get; } =
    [
        "help.altway.pro",
    ];

    /// <summary>
    /// Почему сюда писать нельзя. <c>null</c> означает, что установка опознана как стенд.
    /// </summary>
    /// <remarks>
    /// Вынесено отдельно от <see cref="EnsureWritable"/> и не ходит в сеть: так само правило
    /// проверяемо обычным тестом, без стенда и без прода.
    /// </remarks>
    /// <param name="system">Ответ <c>GET /api/system</c>.</param>
    /// <param name="configuredBaseUrl">Адрес, по которому мы туда пришли.</param>
    public static string? Refusal(BookStackSystemInfo? system, string? configuredBaseUrl)
    {
        var target = HostOf(configuredBaseUrl);
        if (target is null)
            return $"адрес установки не разобрать: \"{configuredBaseUrl}\".";

        var reported = HostOf(system?.BaseUrl);

        // Запрещённые хосты проверяются ПЕРВЫМИ и до всего остального: этот отказ не снимается
        // ничем, включая явно названный идентификатор установки.
        foreach (var forbidden in ForbiddenHosts)
        {
            if (Same(target, forbidden) || Same(reported, forbidden))
                return $"это боевой адрес ({forbidden}). Пишущим проверкам сюда нельзя никогда.";
        }

        if (system is null)
            return "не прочитан GET /api/system, а без него опознать установку нечем.";

        if (string.IsNullOrWhiteSpace(system.InstanceId))
            return "в ответе GET /api/system нет instance_id, опознать установку нечем.";

        var allowedIds = new List<string>(KnownStandInstanceIds);
        var named = Environment.GetEnvironmentVariable(InstanceIdVariable)?.Trim();
        var namedGiven = !string.IsNullOrWhiteSpace(named);
        if (namedGiven)
            allowedIds.Add(named!);

        if (!allowedIds.Any(id => Same(id, system.InstanceId)))
        {
            return
                $"установка с instance_id {system.InstanceId} (адрес {configuredBaseUrl}, " +
                $"base_url {system.BaseUrl}, имя {system.AppName}, версия {system.Version}) в " +
                "белом списке стендов не значится. Известные стенды: " +
                string.Join(", ", KnownStandInstanceIds) + ". Если это правда ваш стенд, назовите " +
                $"его идентификатор целиком в {InstanceIdVariable}; если это боевая установка или " +
                "её копия, менять тут нечего.";
        }

        // Идентификатор совпал, значит это либо известный стенд, либо явно названный. Второй слой:
        // адрес. Для явно названного стенда разрешаем тот хост, по которому мы к нему и пришли:
        // человек уже назвал установку поимённо, и требовать от него ещё и localhost бессмысленно.
        var allowedHosts = new List<string>(KnownStandHosts);
        if (namedGiven)
            allowedHosts.Add(target);

        if (reported is null)
            return $"в ответе GET /api/system нет base_url (шли по адресу {configuredBaseUrl}), сверять нечего.";

        if (!allowedHosts.Any(h => Same(h, target)))
            return $"адрес {configuredBaseUrl} не похож на стендовый: хост {target} не в списке " +
                   string.Join(", ", allowedHosts) + ".";

        if (!allowedHosts.Any(h => Same(h, reported)))
            return $"установка сама себя знает по адресу {system.BaseUrl}, а это не стендовый хост. " +
                   "Так выглядит боевой портал, до которого добрались служебным адресом.";

        return null;
    }

    /// <summary>
    /// Отказывает пишущей проверке, если установка не опознана как стенд. Сети не требует.
    /// </summary>
    /// <exception cref="InvalidOperationException">Установка не из белого списка стендов.</exception>
    public static void EnsureWritable(BookStackSystemInfo? system, string? configuredBaseUrl)
    {
        var refusal = Refusal(system, configuredBaseUrl);
        if (refusal is null)
            return;

        throw new InvalidOperationException(
            "Пишущая проверка BookStack остановлена ДО первой правки: " + refusal +
            " Разница между стендом и боевым порталом это одна строка в " +
            StandGate.UrlVariable + ", а удаление в BookStack мягкое, и уборка проверок добивает " +
            "удалённое из корзины окончательно.");
    }

    /// <summary>
    /// Читает <c>GET /api/system</c> и отказывает, если это не стенд.
    /// </summary>
    /// <remarks>
    /// Ответ намеренно НЕ кэшируется между вызовами: кэш означал бы «однажды разрешили, значит
    /// можно», а разрешение выдаётся конкретной установке по конкретному адресу. Один дешёвый
    /// GET на проверку это честная цена.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Установка не из белого списка стендов.</exception>
    public static async Task<BookStackSystemInfo> EnsureWritableAsync(
        IBookStackSystemApi system, string? configuredBaseUrl, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(system);

        var info = await system.GetAsync(ct).ConfigureAwait(false);
        EnsureWritable(info, configuredBaseUrl);
        return info!;
    }

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var parsed) ? parsed.Host : null;

    private static bool Same(string? left, string? right) =>
        left is not null && right is not null && string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
