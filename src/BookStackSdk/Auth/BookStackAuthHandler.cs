namespace BookStackSdk.Auth;

/// <summary>
/// Подставляет токен BookStack в заголовок <c>Authorization</c>.
/// </summary>
/// <remarks>
/// Формат замерен на стенде 17.08.2026 тремя вариантами одного и того же токена:
/// <list type="bullet">
/// <item><c>Token {id}:{secret}</c> даёт 200;</item>
/// <item><c>Bearer {id}:{secret}</c> даёт 401;</item>
/// <item><c>Token {secret}:{id}</c> (половинки местами) даёт 401 с тем же текстом, что и на
/// несуществующий токен: «No matching API token was found for the provided authorization token».</item>
/// </list>
/// <para>
/// ВАЖНО: значение ставится целиком, одной строкой, через <c>TryAddWithoutValidation</c>.
/// Формально <c>Token</c> это законная схема, и <c>AuthenticationHeaderValue</c> тут прошёл бы,
/// в отличие от MantisBT с его голым токеном. Но разбиение на «схему» и «параметр» означало бы,
/// что строка <c>{id}:{secret}</c> собирается ВТОРОЙ раз, уже здесь, мимо
/// <see cref="BookStackOptions.BuildAuthorizationHeaderValue"/>. Ровно этого мы и избегаем: третий
/// замер выше показывает, во что обходится незамеченная перестановка половинок.
/// </para>
/// </remarks>
public sealed class BookStackAuthHandler : DelegatingHandler
{
    private readonly IBookStackTokenProvider _tokens;

    public BookStackAuthHandler(IBookStackTokenProvider tokens) => _tokens = tokens;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Уже проставленный вызывающим заголовок не трогаем: это путь для проб под чужим токеном.
        if (!request.Headers.Contains("Authorization"))
            request.Headers.TryAddWithoutValidation("Authorization", _tokens.GetAuthorizationHeaderValue());

        return base.SendAsync(request, cancellationToken);
    }
}
