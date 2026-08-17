using BookStackSdk.Abstractions;
using BookStackSdk.Internal;
using BookStackSdk.Models;
using Microsoft.Extensions.Logging;

namespace BookStackSdk.Api;

/// <inheritdoc cref="IBookStackSystemApi"/>
public sealed class BookStackSystemApi : BookStackApiBase, IBookStackSystemApi
{
    public BookStackSystemApi(HttpClient http, ILogger<BookStackSystemApi> logger)
        : base(http, logger)
    {
    }

    /// <inheritdoc />
    public async Task<BookStackSystemInfo?> GetAsync(CancellationToken ct = default)
        => Deserialize<BookStackSystemInfo>((await GetRawAsync("system", ct).ConfigureAwait(false)).Body);
}
