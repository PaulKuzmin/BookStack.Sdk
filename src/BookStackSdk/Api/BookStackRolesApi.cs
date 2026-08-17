using System.Globalization;
using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackRolesApi"/>
public sealed class BookStackRolesApi : BookStackApiBase, IBookStackRolesApi
{
    public BookStackRolesApi(HttpClient http, ILogger<BookStackRolesApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackPage<BookStackRole>> ListAsync(
        int? count = null,
        int? offset = null,
        string? sort = null,
        string? displayName = null,
        CancellationToken ct = default)
    {
        // Фильтра по идентификатору тут нет намеренно: поле id в список маршрута не входит,
        // и такой фильтр сервер молча выбросит, отдав все роли (замерено). Дать его значило бы
        // предложить вызов, который выглядит как отбор, а работает как его отсутствие.
        var query = BookStackQuery.Build(
            ("count", count?.ToString(CultureInfo.InvariantCulture)),
            ("offset", offset?.ToString(CultureInfo.InvariantCulture)),
            ("sort", sort),
            ("filter[display_name]", displayName));

        var raw = await GetRawAsync("roles" + query, ct).ConfigureAwait(false);
        return UnwrapPage<BookStackRole>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackRole?> GetAsync(int id, CancellationToken ct = default)
        => Deserialize<BookStackRole>((await GetRawAsync($"roles/{id}", ct).ConfigureAwait(false)).Body);

    /// <inheritdoc />
    public async Task<BookStackRole?> CreateAsync(
        BookStackCreateRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PostRawAsync("roles", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackRole>(raw.Body);
    }

    /// <inheritdoc />
    public async Task<BookStackRole?> UpdateAsync(
        int id, BookStackUpdateRoleRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var raw = await PutRawAsync($"roles/{id}", request, ct).ConfigureAwait(false);
        return Deserialize<BookStackRole>(raw.Body);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(int id, CancellationToken ct = default)
        => await DeleteRawAsync($"roles/{id}", ct).ConfigureAwait(false);
}
