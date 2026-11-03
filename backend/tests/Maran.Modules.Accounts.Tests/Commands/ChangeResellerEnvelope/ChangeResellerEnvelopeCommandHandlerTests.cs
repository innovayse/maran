using Maran.Modules.Accounts.Commands.ChangeResellerEnvelope;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.ChangeResellerEnvelope;

/// <summary>
/// Behavioral contract of <see cref="ChangeResellerEnvelopeCommandHandler"/>: the envelope is
/// replaced and journalled, lowering it below what is owned is accepted without touching what is
/// owned, and an unknown reseller is refused as not found.
/// </summary>
public sealed class ChangeResellerEnvelopeCommandHandlerTests : IDisposable
{
    /// <summary>The module's context, in memory.</summary>
    private readonly AccountsDbContext _dbContext = AccountsTestContext.Create();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The reseller every test changes, sold ten accounts.</summary>
    private readonly Reseller _reseller = ResellerTestData.Reseller("acme", ResellerTestData.Envelope(maxAccounts: 10));

    /// <summary>Stores the reseller.</summary>
    public ChangeResellerEnvelopeCommandHandlerTests()
    {
        _dbContext.Resellers.Add(_reseller);
        _dbContext.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dbContext.Dispose();
    }

    /// <summary>Runs the handler over this test's context.</summary>
    /// <param name="command">The change to apply.</param>
    private Task<Result<ResellerDetailDto>> RunAsync(ChangeResellerEnvelopeCommand command)
    {
        var handler = new ChangeResellerEnvelopeCommandHandler(
            _dbContext,
            new ResellerUsageReader(_dbContext),
            new AccountAuditJournal(_audit, FakeCurrentUser.Admin()),
            new StubDisplayNames());
        return handler.HandleAsync(command, CancellationToken.None);
    }

    /// <summary>Builds a change of the test reseller's envelope.</summary>
    /// <param name="maxAccounts">The new account count.</param>
    /// <param name="resellerId">The reseller to change; the test reseller unless a test aims elsewhere.</param>
    private ChangeResellerEnvelopeCommand Change(int maxAccounts, Guid? resellerId = null)
    {
        return new ChangeResellerEnvelopeCommand(
            ResellerEnvelopeDtoTestData.Valid() with { MaxAccounts = maxAccounts },
            resellerId ?? _reseller.Id,
            "203.0.113.7",
            "unit-tests");
    }

    /// <summary>Builds an account of the test reseller on <paramref name="plan"/>.</summary>
    /// <param name="name">The account's name.</param>
    /// <param name="plan">The plan the account is on.</param>
    private Account OwnedAccount(string name, Plan plan)
    {
        return new Account(
            Guid.NewGuid(),
            name,
            $"{name}.example.com",
            plan.Id,
            ResellerTestData.CreatedAt,
            resellerId: _reseller.Id);
    }

    /// <summary>A change replaces the stored envelope, moves the allocation revision and answers it.</summary>
    [Fact]
    public async Task A_change_replaces_the_stored_envelope_and_answers_the_new_one()
    {
        var revisionBefore = _reseller.AllocationRevision;

        var result = await RunAsync(Change(maxAccounts: 4));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.Envelope.MaxAccounts);
        var stored = await _dbContext.Resellers.AsNoTracking().SingleAsync();
        Assert.Equal(4, stored.Envelope.MaxAccounts);
        Assert.Equal(revisionBefore + 1, stored.AllocationRevision);
    }

    /// <summary>A change is journalled as a success naming the reseller.</summary>
    [Fact]
    public async Task A_change_is_journalled_as_a_success_naming_the_reseller()
    {
        await RunAsync(Change(maxAccounts: 4));

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerEnvelopeChanged, entry.Action);
        Assert.Equal("acme", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
    }

    /// <summary>Lowering the envelope below what the reseller owns is accepted and edits no account or plan.</summary>
    [Fact]
    public async Task Lowering_the_envelope_below_what_is_owned_is_accepted_and_edits_nothing_owned()
    {
        var plan = Plan.Define(Guid.NewGuid(), "Basic", 1_000, 1, 1, 1, 0, 0, 1, resellerId: _reseller.Id);
        _dbContext.Plans.Add(plan);
        _dbContext.Accounts.AddRange(OwnedAccount("one", plan), OwnedAccount("two", plan));
        await _dbContext.SaveChangesAsync();

        var result = await RunAsync(Change(maxAccounts: 1));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Envelope.MaxAccounts);
        Assert.Equal(2, result.Value.AccountCount);
        Assert.Equal(2_000, result.Value.DiskAllocatedMb);
        Assert.Equal(2, await _dbContext.Accounts.CountAsync());
        Assert.Equal(1_000, (await _dbContext.Plans.SingleAsync()).DiskQuotaMb);
    }

    /// <summary>A reseller that does not exist is refused as not found and journalled by the id asked for.</summary>
    [Fact]
    public async Task A_reseller_that_does_not_exist_is_refused_as_not_found()
    {
        var missing = Guid.NewGuid();

        var result = await RunAsync(Change(maxAccounts: 4, missing));

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(10, (await _dbContext.Resellers.AsNoTracking().SingleAsync()).Envelope.MaxAccounts);
        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(missing.ToString(), entry.Subject);
        Assert.False(entry.Succeeded);
    }
}
