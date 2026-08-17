using Microsoft.Extensions.Options;

namespace BookStackSdk.Auth;

/// <summary>
/// Источник значения заголовка <c>Authorization</c>. Вынесен интерфейсом, чтобы токен можно было
/// ротировать, не пересобирая клиента.
/// </summary>
/// <remarks>
/// ВАЖНО: интерфейс отдаёт ГОТОВОЕ значение заголовка целиком, а не пару «идентификатор и секрет».
/// Это следствие того же решения, что и раздельные поля в настройках: пара, доехавшая до места
/// склейки, рано или поздно склеится в обратном порядке, а BookStack на перепутанные половинки
/// отвечает тем же 401, что и на любой негодный токен (замерено на стенде 17.08.2026). Склейка
/// живёт в единственном месте: <see cref="BookStackOptions.BuildAuthorizationHeaderValue"/>.
/// </remarks>
public interface IBookStackTokenProvider
{
    /// <summary>Значение заголовка <c>Authorization</c> целиком, вида <c>Token {id}:{secret}</c>.</summary>
    string GetAuthorizationHeaderValue();
}

/// <summary>Токен из настроек. Умолчание для боевого и стендового применения.</summary>
public sealed class BookStackOptionsTokenProvider : IBookStackTokenProvider
{
    private readonly IOptionsMonitor<BookStackOptions> _options;

    public BookStackOptionsTokenProvider(IOptionsMonitor<BookStackOptions> options) => _options = options;

    /// <inheritdoc />
    public string GetAuthorizationHeaderValue() => _options.CurrentValue.BuildAuthorizationHeaderValue();
}
