using System.Text;
using BookStackSdk.Errors;
using BookStackTools;

namespace BookStackDocsPush;

/// <summary>Точка входа: аргументы, проверка портала, выкладка.</summary>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitGuard = 2;
    private const int ExitFailed = 3;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        PushArgs parsed;
        try
        {
            var maybe = PushArgs.Parse(args);
            if (maybe is null)
            {
                Console.WriteLine(PushArgs.Usage);
                return ExitOk;
            }

            parsed = maybe;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(PushArgs.Usage);
            return ExitUsage;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
            Console.Error.WriteLine("Прерывание: обрываю текущий запрос и выхожу.");
        };

        // Сухой прогон не ходит на портал вовсе: ни токена, ни сети он не требует, а проверить
        // раскладку сотни страниц надо ДО того, как заводить доступы.
        if (parsed.DryRun)
        {
            try
            {
                var scanned = DocsPlan.Scan(parsed);
                DocsPlan.Print(scanned, parsed);
                DocsPlan.Validate(scanned, parsed);
                DocsPlan.PrintLinks(scanned, parsed);
                Console.WriteLine();
                Console.WriteLine("--dry-run: на портал не ходил, на диске ничего не менял.");
                return ExitOk;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                Console.Error.WriteLine(e.Message);
                return ExitFailed;
            }
        }

        try
        {
            using var portal = BookStackEndpoint.Create(
                parsed.PortalUrl, parsed.Timeout, parsed.Verbose, string.Empty, "TO");

            if (!await CheckPortalAsync(portal, parsed, stop.Token))
                return ExitGuard;

            await new DocsPush(portal, parsed).RunAsync(stop.Token);
            return ExitOk;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Прервано. Что успело выложиться, осталось на портале.");
            return ExitFailed;
        }
        catch (BookStackApiException e)
        {
            Console.Error.WriteLine($"BookStack отказал: {e.Message}");
            return ExitFailed;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e.Message);
            return ExitFailed;
        }
    }

    /// <summary>
    /// Проверяет, что портал жив, токен принят, и это тот самый портал.
    /// </summary>
    /// <remarks>
    /// Замок по <c>instance_id</c> необязателен, но задавать его стоит: инструмент пишет сотнями
    /// страниц, а разница между стендом и боевым порталом — одна строка в командной строке.
    /// </remarks>
    private static async Task<bool> CheckPortalAsync(
        BookStackEndpoint portal, PushArgs args, CancellationToken ct)
    {
        var info = await portal.System.GetAsync(ct);
        Console.WriteLine($"Портал {portal.BaseUrl}: {info?.Version} ({info?.InstanceId})");

        if (string.IsNullOrWhiteSpace(args.ExpectInstance)
            || string.Equals(info?.InstanceId, args.ExpectInstance.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Console.Error.WriteLine(
            $"Это не тот портал: ждали instance_id {args.ExpectInstance}, а он {info?.InstanceId}.");

        return false;
    }
}
