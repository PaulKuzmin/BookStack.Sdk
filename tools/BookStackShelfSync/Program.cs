using System.Text;
using BookStackSdk.Errors;
using BookStackTools;

namespace BookStackShelfSync;

/// <summary>
/// Точка входа: разбор аргументов, замки перед первой правкой, запуск переноса.
/// </summary>
/// <remarks>
/// Замков два, и оба стоят ДО первого изменяющего вызова. Первый — совпадение адресов, ловится при
/// разборе аргументов. Второй — совпадение идентификаторов установки: адреса могут быть разными и
/// вести в одно и то же место (прокси, зеркало, псевдоним домена), и тогда «перенос на стенд»
/// оказался бы переливанием прода в прод. Ни имя, ни версия таким замком быть не могут, а
/// <c>instance_id</c> уникален на установку.
/// </remarks>
internal static class Program
{
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitGuard = 2;
    private const int ExitFailed = 3;

    /// <summary>Часть книг переехала, часть нет: не успех, но и не «всё пропало».</summary>
    private const int ExitPartial = 4;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        SyncArgs parsed;
        try
        {
            var maybe = SyncArgs.Parse(args);
            if (maybe is null)
            {
                Console.WriteLine(SyncArgs.Usage);
                return ExitOk;
            }

            parsed = maybe;
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            Console.Error.WriteLine(e.Message);
            Console.Error.WriteLine();
            Console.Error.WriteLine(SyncArgs.Usage);
            return ExitUsage;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
            Console.Error.WriteLine("Прерывание: обрываю текущий запрос и выхожу.");
        };

        try
        {
            using var source = BookStackEndpoint.Create(parsed.FromUrl, parsed.Timeout, parsed.Verbose, "FROM");
            using var target = BookStackEndpoint.Create(parsed.ToUrl, parsed.Timeout, parsed.Verbose, "TO");

            if (!await CheckInstancesAsync(source, target, parsed, stop.Token))
                return ExitGuard;

            await new ShelfSync(source, target, parsed).RunAsync(stop.Token);
            return ExitOk;
        }
        catch (PartialTransferException e)
        {
            // Сообщение уже напечатано подробно, тут только код возврата для вызывающего скрипта.
            Console.Error.WriteLine(e.Message);
            return ExitPartial;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Прервано. Что успело переехать, осталось на приёмнике.");
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

    /// <summary>Сверяет установки и заодно проверяет, что оба токена вообще пускают.</summary>
    private static async Task<bool> CheckInstancesAsync(
        BookStackEndpoint source, BookStackEndpoint target, SyncArgs args, CancellationToken ct)
    {
        // GET /api/system дёшев, прав не требует и отвечает 401 на негодный токен: проверка живости
        // и проверка токена одним запросом, до того как что-то менять.
        var from = await source.System.GetAsync(ct);
        var to = await target.System.GetAsync(ct);

        Console.WriteLine($"Источник {source.BaseUrl}: {from?.Version} ({from?.InstanceId})");
        Console.WriteLine($"Приёмник {target.BaseUrl}: {to?.Version} ({to?.InstanceId})");
        Console.WriteLine();

        if (from?.InstanceId is null || to?.InstanceId is null
            || !string.Equals(from.InstanceId, to.InstanceId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (args.AllowSameInstance)
        {
            Console.WriteLine("Источник и приёмник это ОДНА установка, но так и просили.");
            return true;
        }

        Console.Error.WriteLine(
            "Источник и приёмник это одна и та же установка (совпал instance_id): адреса разные, "
            + "а место одно. Если это правда нужно, добавьте --allow-same-instance.");

        return false;
    }
}
