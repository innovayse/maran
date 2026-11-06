using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetApiKeySelf;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Identity.Tests.Queries.GetApiKeySelf;

/// <summary>Behavioural contract of <see cref="GetApiKeySelfQueryHandler"/>.</summary>
public sealed class GetApiKeySelfQueryHandlerTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>Releases what the fixture allocated.</summary>
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>The caller's own key is described with its effective scopes, which include the implicit self scope.</summary>
    [Fact]
    public async Task The_callers_own_key_is_described_with_its_effective_scopes()
    {
        var (key, _) = await ApiKeySeed.AddAsync(_context, Now.AddDays(-1), scopes: [ApiKeyScopes.ServerRead], expiresAt: Now.AddDays(30));
        key.RecordUse(Now.AddHours(-2), "203.0.113.7");
        await _context.SaveChangesAsync();

        var result = await new GetApiKeySelfQueryHandler(_context).HandleAsync(new GetApiKeySelfQuery(key.Id), CancellationToken.None);

        Assert.Equal(key.Id, result.Value.Id);
        Assert.Equal(key.Prefix, result.Value.Prefix);
        Assert.Equal([ApiKeyScopes.SelfRead, ApiKeyScopes.ServerRead], result.Value.Scopes);
        Assert.Equal(Now.AddDays(30), result.Value.ExpiresAt);
        Assert.Equal(Now.AddHours(-2), result.Value.LastUsedAt);
    }

    /// <summary>Asking for another key id, or one that no longer exists, is not found.</summary>
    [Fact]
    public async Task A_key_id_with_no_row_is_not_found()
    {
        var result = await new GetApiKeySelfQueryHandler(_context).HandleAsync(new GetApiKeySelfQuery(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal("ApiKeyNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
