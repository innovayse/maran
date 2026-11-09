using Maran.Modules.Accounts.Commands.SetResellerBranding;
using Maran.Modules.Accounts.Common;
using Maran.Modules.Accounts.Domain.Entities;
using Maran.Modules.Accounts.Persistence;
using Maran.Modules.Accounts.Services;
using Maran.Modules.Accounts.Tests.TestSupport;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Maran.Modules.Accounts.Tests.Commands.SetResellerBranding;

/// <summary>
/// Behavioral contract of <see cref="SetResellerBrandingCommandHandler"/>: the calling reseller's
/// branding is created, replaced or removed and journalled, another reseller's is never touched, and a
/// suspended reseller or a caller that is no reseller is refused.
/// </summary>
public sealed class SetResellerBrandingCommandHandlerTests : IDisposable
{
    /// <summary>The instant the clock reports.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The name of the in-memory database every context of one test opens.</summary>
    private readonly string _database = Guid.NewGuid().ToString();

    /// <summary>Everything the handler journals.</summary>
    private readonly RecordingAuditWriter _audit = new();

    /// <summary>The reseller that signs in.</summary>
    private readonly Reseller _alpha = ResellerTestData.Reseller("alpha");

    /// <summary>A reseller whose branding the first one must never reach.</summary>
    private readonly Reseller _beta = ResellerTestData.Reseller("beta");

    /// <summary>An administrator's context, which sees every row, used to seed and to assert.</summary>
    private readonly AccountsDbContext _admin;

    /// <summary>Stores both resellers and a branding for the second.</summary>
    public SetResellerBrandingCommandHandlerTests()
    {
        _admin = AccountsTestContext.CreateOn(_database, FakeCurrentUser.Admin());
        _admin.Resellers.AddRange(_alpha, _beta);
        _admin.ResellerBrandings.Add(new ResellerBranding(_beta.Id, "Beta", "#2468dc", null, Now));
        _admin.SaveChanges();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _admin.Dispose();
    }

    /// <summary>Runs the handler as <paramref name="caller"/>.</summary>
    /// <param name="caller">The principal the request runs as.</param>
    /// <param name="command">The command to handle.</param>
    private async Task<Result<ResellerBrandingDto>> RunAsync(FakeCurrentUser caller, SetResellerBrandingCommand command)
    {
        await using var dbContext = AccountsTestContext.CreateOn(_database, caller);
        var handler = new SetResellerBrandingCommandHandler(
            dbContext,
            new ResellerScope(dbContext, caller),
            new AccountAuditJournal(_audit, caller),
            new FakeClock(Now));
        return await handler.HandleAsync(command, CancellationToken.None);
    }

    /// <summary>Builds a command carrying the audit stamp.</summary>
    /// <param name="displayName">The name.</param>
    /// <param name="accentColor">The colour.</param>
    /// <param name="logo">The logo.</param>
    private static SetResellerBrandingCommand Set(string? displayName, string? accentColor, string? logo)
    {
        return new SetResellerBrandingCommand(displayName, accentColor, logo, "203.0.113.7", "unit-tests");
    }

    /// <summary>Setting the branding of a reseller with none stores and answers the three values.</summary>
    [Fact]
    public async Task Setting_the_branding_stores_and_answers_the_three_values()
    {
        var result = await RunAsync(
            FakeCurrentUser.Reseller(_alpha.Id, "alpha"),
            Set("  Alpha Hosting ", "#2468DC", BrandingTestData.PngLogo));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ResellerBrandingDto("Alpha Hosting", "#2468dc", BrandingTestData.PngLogo), result.Value);
        var stored = await _admin.ResellerBrandings.AsNoTracking().SingleAsync(b => b.ResellerId == _alpha.Id);
        Assert.Equal("Alpha Hosting", stored.DisplayName);
        Assert.Equal(Now, stored.UpdatedAt);
    }

    /// <summary>Setting again replaces all three values, so a value left out is cleared.</summary>
    [Fact]
    public async Task Setting_again_replaces_every_value()
    {
        var caller = FakeCurrentUser.Reseller(_alpha.Id, "alpha");
        await RunAsync(caller, Set("Alpha", "#2468dc", BrandingTestData.PngLogo));

        var result = await RunAsync(caller, Set("Alpha two", null, null));

        Assert.Equal(new ResellerBrandingDto("Alpha two", null, null), result.Value);
        Assert.Equal(1, await _admin.ResellerBrandings.CountAsync(b => b.ResellerId == _alpha.Id));
    }

    /// <summary>Setting nothing removes the row, and a blank name counts as nothing.</summary>
    [Fact]
    public async Task Setting_nothing_removes_the_branding()
    {
        var caller = FakeCurrentUser.Reseller(_alpha.Id, "alpha");
        await RunAsync(caller, Set("Alpha", null, null));

        var result = await RunAsync(caller, Set("   ", null, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(new ResellerBrandingDto(null, null, null), result.Value);
        Assert.False(await _admin.ResellerBrandings.AnyAsync(b => b.ResellerId == _alpha.Id));
    }

    /// <summary>A reseller's change never reaches another reseller's branding.</summary>
    [Fact]
    public async Task A_change_never_reaches_another_resellers_branding()
    {
        await RunAsync(FakeCurrentUser.Reseller(_alpha.Id, "alpha"), Set("Alpha", null, null));
        await RunAsync(FakeCurrentUser.Reseller(_alpha.Id, "alpha"), Set(null, null, null));

        var beta = await _admin.ResellerBrandings.AsNoTracking().SingleAsync(b => b.ResellerId == _beta.Id);
        Assert.Equal("Beta", beta.DisplayName);
        Assert.Equal("#2468dc", beta.AccentColor);
    }

    /// <summary>A change is journalled as a success naming the reseller and carrying no value.</summary>
    [Fact]
    public async Task A_change_is_journalled_as_a_success_naming_the_reseller_and_no_value()
    {
        await RunAsync(FakeCurrentUser.Reseller(_alpha.Id, "alpha"), Set("Alpha", "#2468dc", BrandingTestData.PngLogo));

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal(AccountAuditActions.ResellerBrandingChanged, entry.Action);
        Assert.Equal("alpha", entry.Subject);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.True(entry.Succeeded);
        Assert.DoesNotContain("data:", entry.Subject);
    }

    /// <summary>A suspended reseller is refused with the suspension code, nothing is stored and the refusal is journalled.</summary>
    [Fact]
    public async Task A_suspended_reseller_is_refused_and_the_refusal_is_journalled()
    {
        _alpha.Suspend();
        await _admin.SaveChangesAsync();

        var result = await RunAsync(FakeCurrentUser.Reseller(_alpha.Id, "alpha"), Set("Alpha", null, null));

        Assert.Equal("ResellerSuspended", result.Error!.Code);
        Assert.Equal(ErrorType.Conflict, result.Error.Type);
        Assert.False(await _admin.ResellerBrandings.AnyAsync(b => b.ResellerId == _alpha.Id));
        var entry = Assert.Single(_audit.Entries);
        Assert.False(entry.Succeeded);
    }

    /// <summary>A caller that is not a reseller gets the not-found answer, and nothing is stored.</summary>
    [Fact]
    public async Task A_caller_that_is_not_a_reseller_is_refused_as_not_found()
    {
        var result = await RunAsync(FakeCurrentUser.Admin(), Set("Admin", null, null));

        Assert.Equal("ResellerNotFound", result.Error!.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
        Assert.Equal(1, await _admin.ResellerBrandings.CountAsync());
    }
}
