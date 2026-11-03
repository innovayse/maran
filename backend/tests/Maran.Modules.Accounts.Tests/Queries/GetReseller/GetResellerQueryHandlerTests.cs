using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Domain.Enums;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Queries.GetReseller;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;

namespace Maran.Modules.Accounts.Tests.Queries.GetReseller;

/// <summary>
/// Behavioral contract of <see cref="GetResellerQueryHandler"/>: a reseller is answered with its
/// envelope and usage, and an unknown id is not found.
/// </summary>
public sealed class GetResellerQueryHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context.</summary>
    /// <param name="resellerId">The reseller to read.</param>
    private Task<Result<ResellerDetailDto>> RunAsync(Guid resellerId)
    {
        return new GetResellerQueryHandler(
                _dbContext,
                new ResellerUsageReader(_dbContext),
                new StubDisplayNames().With("ResellerStatusActive", "Live"))
            .HandleAsync(new GetResellerQuery(resellerId), CancellationToken.None);
    }

    /// <summary>A reseller is answered with its identity, language, envelope and what it owns.</summary>
    [Fact]
    public async Task A_reseller_is_answered_with_its_identity_language_envelope_and_what_it_owns()
    {
        var reseller = ResellerTestData.Reseller(
            "acme",
            ResellerTestData.Envelope(maxAccounts: 8, features: AccountFeature.Backups));
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 2_500, 1, 1, 1, 0, 0, 1, resellerId: reseller.Id);
        _dbContext.Resellers.Add(reseller);
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.Add(new Account(
            Guid.NewGuid(),
            "owned",
            "owned.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: reseller.Id));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync(reseller.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(reseller.Id, result.Value.Id);
        Assert.Equal("acme", result.Value.Name);
        Assert.Equal("en", result.Value.Language);
        Assert.Equal(ResellerStatus.Active, result.Value.Status);
        Assert.Equal("Live", result.Value.StatusName);
        Assert.Equal(8, result.Value.Envelope.MaxAccounts);
        Assert.Equal([AccountFeature.Backups], result.Value.Envelope.Features);
        Assert.Equal(1, result.Value.AccountCount);
        Assert.Equal(1, result.Value.PlanCount);
        Assert.Equal(2_500, result.Value.DiskAllocatedMb);
    }

    /// <summary>An id that names no reseller is answered as not found.</summary>
    [Fact]
    public async Task An_id_that_names_no_reseller_is_answered_as_not_found()
    {
        _dbContext.Resellers.Add(ResellerTestData.Reseller("acme"));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync(Guid.NewGuid());

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }
}
