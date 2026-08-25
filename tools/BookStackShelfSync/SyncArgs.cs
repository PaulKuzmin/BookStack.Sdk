namespace BookStackShelfSync;

/// <summary>Разобранная командная строка.</summary>
/// <param name="FromUrl">Адрес установки-источника без хвостового слэша.</param>
/// <param name="ToUrl">Адрес установки-приёмника.</param>
/// <param name="ShelfSlug">Слаг переносимой полки: последний кусок адреса <c>/shelves/…</c>.</param>
/// <param name="WorkDir">Куда складывать выгруженные архивы.</param>
/// <param name="Replace">Сносить ли на приёмнике книги с совпадающим слагом.</param>
/// <param name="RewriteLinks">Править ли ссылки на источник в перенесённых страницах.</param>
/// <param name="DryRun">Только показать план, ничего не менять.</param>
/// <param name="AllowSameInstance">Снять замок «источник и приёмник это одна установка».</param>
/// <param name="Verbose">Показывать запросы SDK.</param>
/// <param name="Timeout">Таймаут одного запроса.</param>
internal sealed record SyncArgs(
    string FromUrl,
    string ToUrl,
    string ShelfSlug,
    string WorkDir,
    bool Replace,
    bool RewriteLinks,
    bool DryRun,
    bool AllowSameInstance,
    bool Verbose,
    TimeSpan Timeout)
{
    /// <summary>Как пользоваться. Печатается при <c>--help</c> и при ошибке разбора.</summary>
    public const string Usage = """
        Перенос полки BookStack (её книг) с одной установки на другую.

          bookstack-shelf-sync --from <адрес> --to <адрес> --shelf <слаг> [ключи]

        Обязательное:
          --from <адрес>     откуда, например https://help.altway.pro
          --to <адрес>       куда, например https://test.help.altway.pro
          --shelf <слаг>     слаг полки из адреса /shelves/<слаг>

        Ключи:
          --dry-run          показать план и выйти, ничего не меняя
          --replace          снести на приёмнике книги с тем же слагом, добив их из корзины
                             (иначе восстановление даст вторую книгу с тем же слагом)
          --rewrite-links    заменить в перенесённых страницах адрес источника на адрес приёмника
                             (только страницы в markdown, см. README)
          --allow-same-instance  разрешить перенос внутри одной установки
          --work-dir <путь>  куда класть архивы (по умолчанию каталог во временных файлах)
          --timeout <сек>    таймаут одного запроса, по умолчанию 600
          --verbose          показывать запросы к API
          --help             эта справка

        Токены берутся из переменных окружения, половинками, как их выдаёт BookStack:
          BOOKSTACK_FROM_TOKEN_ID, BOOKSTACK_FROM_TOKEN_SECRET
          BOOKSTACK_TO_TOKEN_ID,   BOOKSTACK_TO_TOKEN_SECRET

        Токену приёмника нужно право на импорт содержимого, а для --replace ещё и корзина:
        она требует сразу двух прав, на управление настройками и на управление правами.
        """;

    /// <summary>
    /// Разбирает аргументы.
    /// </summary>
    /// <remarks>
    /// Разбор ручной и без библиотек: ключей десяток, а лишняя зависимость в инструменте, который
    /// запускают раз в полгода, переживёт сам инструмент.
    /// </remarks>
    /// <returns>Разобранные аргументы либо <c>null</c>, если просили справку.</returns>
    /// <exception cref="ArgumentException">Аргументы неполны или непонятны.</exception>
    public static SyncArgs? Parse(string[] args)
    {
        string? from = null, to = null, shelf = null, workDir = null;
        bool replace = false, rewrite = false, dry = false, same = false, verbose = false;
        var timeout = TimeSpan.FromSeconds(600);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help" or "-h" or "/?":
                    return null;
                case "--from":
                    from = Next(args, ref i);
                    break;
                case "--to":
                    to = Next(args, ref i);
                    break;
                case "--shelf":
                    shelf = Next(args, ref i);
                    break;
                case "--work-dir":
                    workDir = Next(args, ref i);
                    break;
                case "--timeout":
                    timeout = ParseSeconds(Next(args, ref i));
                    break;
                case "--replace":
                    replace = true;
                    break;
                case "--rewrite-links":
                    rewrite = true;
                    break;
                case "--dry-run":
                    dry = true;
                    break;
                case "--allow-same-instance":
                    same = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                default:
                    throw new ArgumentException($"Непонятный аргумент: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(from)) throw new ArgumentException("Не задан --from.");
        if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("Не задан --to.");
        if (string.IsNullOrWhiteSpace(shelf)) throw new ArgumentException("Не задан --shelf.");

        from = from.TrimEnd('/');
        to = to.TrimEnd('/');

        // Замок на самую дорогую опечатку: перепутанные местами --from и --to. Одинаковые адреса
        // ловятся здесь, разные установки с одним instance_id — в Program, уже по ответу сервера.
        if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("--from и --to это один и тот же адрес.");

        workDir ??= System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "bookstack-shelf-sync", shelf);

        return new SyncArgs(from, to, shelf, workDir, replace, rewrite, dry, same, verbose, timeout);
    }

    /// <summary>Таймаут в секундах. Отдельным методом ради внятного отказа на чепухе.</summary>
    /// <remarks>
    /// Своя проверка вместо голого разбора: число вне разумных границ (ноль, минус, миллиард)
    /// прошло бы разбор и обернулось либо мгновенным обрывом каждого запроса, либо повисанием
    /// навсегда, а переполнение вылетело бы вовсе без объяснения.
    /// </remarks>
    private static TimeSpan ParseSeconds(string value)
    {
        const int Max = 24 * 60 * 60;

        if (!int.TryParse(value, out var seconds) || seconds < 1 || seconds > Max)
            throw new ArgumentException($"--timeout ждёт целое число секунд от 1 до {Max}, а не «{value}».");

        return TimeSpan.FromSeconds(seconds);
    }

    private static string Next(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"У аргумента {args[i]} нет значения.");

        return args[++i];
    }
}
