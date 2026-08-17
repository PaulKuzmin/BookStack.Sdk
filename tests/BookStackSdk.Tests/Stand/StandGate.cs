using System;

namespace BookStackSdk.Tests.Stand;

/// <summary>
/// Гейт стендовых проверок: адрес и токен живого BookStack, признак доступности и НАЗВАННАЯ
/// причина, когда стенда нет.
/// </summary>
/// <remarks>
/// Зачем гейт вообще. Стендовые проверки ходят в докерный BookStack, который поднят не на каждой
/// машине и не в каждом прогоне (сборочный агент, чужая рабочая станция, прогон без докера).
/// Без гейта такой прогон краснел бы от НЕНАСТРОЕННОЙ МАШИНЫ, а красный прогон обязан означать
/// сломанный код. Поэтому по умолчанию стендовые проверки пропускаются с причиной, которую видно
/// в отчёте (<see cref="StandFactAttribute"/>), а не молча.
/// <para>
/// Токен живёт в переменной окружения и в исходники не попадает: в этом проекте ключ уже утекал
/// ровно так, литералом в файле, и был отозван по хешу.
/// </para>
/// <para>
/// ⚠️ Токен BookStack состоит из ДВУХ половинок, и переменная несёт их одной строкой через
/// двоеточие, ровно как их показывает сам BookStack. Разбор на половинки живёт здесь и только
/// здесь: <see cref="BookStackOptions"/> держит их отдельными полями намеренно, потому что при
/// перестановке половинок местами приходит 401 с ровно тем же текстом, что на просроченный,
/// отозванный и чужой токен. По ответу перестановку не опознать, поэтому лучше отвергнуть кривое
/// значение здесь, где ещё видно исходную строку.
/// </para>
/// <para>
/// Значения читаются ОДИН раз, при первом обращении к классу. Причина: тот же признак доступности
/// читает атрибут во время ОБНАРУЖЕНИЯ тестов, а фикстура и сами проверки читают его во время
/// ПРОГОНА. Переменная, поменявшаяся между этими двумя моментами, дала бы отчёт, в котором часть
/// проверок пропущена по одной причине, а часть упала по прямо противоположной.
/// </para>
/// </remarks>
public static class StandGate
{
    /// <summary>Переменная с базовым адресом стенда, например <c>http://localhost:6875</c>.</summary>
    public const string UrlVariable = "ALTWAY_TESTS_BOOKSTACK_URL";

    /// <summary>
    /// Переменная с токеном REST API в виде <c>{id}:{secret}</c>, без слова <c>Token</c> впереди:
    /// схему подставляет сам SDK.
    /// </summary>
    public const string TokenVariable = "ALTWAY_TESTS_BOOKSTACK_TOKEN";

    /// <summary>
    /// Подсказка, которая идёт вместе с любой причиной недоступности. Пишется один раз здесь,
    /// чтобы в отчёте прогона было видно не только «стенда нет», но и что именно сделать.
    /// </summary>
    public const string Hint =
        "Поднять стенд: docker compose up -d в каталоге стенда MantisBT и BookStack. Проверить: " +
        "curl -H \"Authorization: Token <id>:<secret>\" http://localhost:6875/api/system (ждём 200 " +
        "и instance_id). Задать " + UrlVariable + "=http://localhost:6875 и " +
        TokenVariable + "=<id>:<secret> из .api-tokens рядом с docker-compose.yml; в исходники " +
        "токен не кладём. Чтобы ненастроенный стенд КРАСНЕЛ, а не пропускался, задать " +
        StrictStand.Variable + "=1.";

    static StandGate()
    {
        var url = Environment.GetEnvironmentVariable(UrlVariable)?.Trim();
        var token = Environment.GetEnvironmentVariable(TokenVariable)?.Trim();

        var hasUrl = !string.IsNullOrWhiteSpace(url);
        var hasToken = !string.IsNullOrWhiteSpace(token);

        if (!hasUrl && !hasToken)
        {
            Unavailable = $"не заданы переменные {UrlVariable} и {TokenVariable}. {Hint}";
            return;
        }

        if (!hasUrl)
        {
            Unavailable = $"токен задан, а адреса нет: не заполнена {UrlVariable}. {Hint}";
            return;
        }

        if (!hasToken)
        {
            Unavailable = $"адрес задан ({url}), а токена нет: не заполнена {TokenVariable}. {Hint}";
            return;
        }

        // Адрес проверяем ЗДЕСЬ, а не там, где он впервые понадобится. Значение вида
        // "localhost:6875" (без схемы) в противном случае доехало бы до сборки клиента и вылезло
        // бы исключением про формат Uri из глубины DI, где про переменную окружения уже никто не
        // помнит.
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps))
        {
            Unavailable =
                $"в {UrlVariable} лежит \"{url}\", а нужен абсолютный адрес вида " +
                $"http://localhost:6875 (со схемой). {Hint}";
            return;
        }

        // ⚠️ Схему в переменной отвергаем сразу. SDK собирает заголовок сам
        // (BookStackOptions.BuildAuthorizationHeaderValue), и значение, скопированное вместе со
        // словом Token из примера curl, дало бы заголовок "Token Token id:secret", то есть тот же
        // самый неотличимый 401.
        if (token!.StartsWith("Token ", StringComparison.OrdinalIgnoreCase)
            || token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            Unavailable =
                $"в {TokenVariable} лежит токен вместе со схемой (\"{token.Split(' ')[0]} ...\"). " +
                "Схему подставляет сам SDK, в переменной нужны только две половинки через " +
                $"двоеточие. {Hint}";
            return;
        }

        var halves = token.Split(':');
        if (halves.Length != 2
            || string.IsNullOrWhiteSpace(halves[0])
            || string.IsNullOrWhiteSpace(halves[1]))
        {
            Unavailable =
                $"в {TokenVariable} лежит значение, не похожее на токен BookStack: нужны РОВНО две " +
                "половинки через двоеточие, id и secret. Обе выглядят одинаково (32 символа), " +
                "поэтому по ответу сервера ошибку в них не опознать: и перестановка местами, и " +
                $"отозванный токен дают один и тот же 401. {Hint}";
            return;
        }

        Url = parsed.GetLeftPart(UriPartial.Authority) + parsed.AbsolutePath.TrimEnd('/');
        TokenId = halves[0].Trim();
        TokenSecret = halves[1].Trim();
        IsConfigured = true;
        Unavailable = string.Empty;
    }

    /// <summary>Базовый адрес стенда без хвостового слэша. <c>null</c>, когда стенд не настроен.</summary>
    public static string? Url { get; }

    /// <summary>Первая половина токена (id). <c>null</c>, когда стенд не настроен.</summary>
    public static string? TokenId { get; }

    /// <summary>Вторая половина токена (secret). <c>null</c>, когда стенд не настроен.</summary>
    public static string? TokenSecret { get; }

    /// <summary>Стенд объявлен и настройки пригодны к употреблению.</summary>
    /// <remarks>
    /// «Объявлен» не значит «отвечает» и не значит «это точно стенд». Живость проверяет
    /// <see cref="StandFixture"/>, а то, что по адресу именно стенд, а не боевой портал,
    /// проверяет <see cref="ProductionGuard"/>. Это три разных отказа с разной починкой.
    /// </remarks>
    public static bool IsConfigured { get; }

    /// <summary>
    /// Почему стенд недоступен, вместе с подсказкой. Пустая строка, когда стенд настроен.
    /// </summary>
    public static string Unavailable { get; } = "гейт стенда не инициализирован";

    /// <summary>Куда именно ходят проверки. Для сообщений об ошибках и для журнала уборки.</summary>
    public static string Describe() => IsConfigured ? $"{UrlVariable}={Url}" : "стенд не настроен";
}
