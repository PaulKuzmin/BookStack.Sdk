using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackUsersApi"/>
public sealed class BookStackUsersApi : BookStackApiBase, IBookStackUsersApi
{
    public BookStackUsersApi(HttpClient http, ILogger<BookStackUsersApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackUser>> ListAsync(
        BookStackUserListQuery? query = null, CancellationToken ct = default)
    {
        var raw = await GetRawAsync("users" + (query?.ToQueryString() ?? string.Empty), ct).ConfigureAwait(false);
        return UnwrapPage<BookStackUser>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackUser?> GetAsync(int id, CancellationToken ct = default)
        => Deserialize<BookStackUser>((await GetRawAsync($"users/{id}", ct).ConfigureAwait(false)).Body);

    /// <inheritdoc />
    public async Task<BookStackUser?> CreateAsync(
        BookStackCreateUserRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PostRawAsync("users", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackUser>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackUser?> UpdateAsync(
        int id, BookStackUpdateUserRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PutRawAsync($"users/{id}", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackUser>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(
        int id, BookStackDeleteUserRequest? request = null, CancellationToken ct = default)
    {
        // Тело у DELETE собирается вручную, потому что общий DeleteRawAsync тела не принимает,
        // и правильно делает: у всех прочих маршрутов тела при удалении нет. Здесь оно есть,
        // и это не наша выдумка: UserApiController::delete читает migrate_ownership_id именно
        // из тела запроса. Проверено вживую, значение доезжает.
        using var message = new HttpRequestMessage(HttpMethod.Delete, $"users/{id}");

        if (request is not null)
            message.Content = BookStackJson.CreateContent(request);

        await SendRawAsync(message, ct).ConfigureAwait(false);
    }
}
