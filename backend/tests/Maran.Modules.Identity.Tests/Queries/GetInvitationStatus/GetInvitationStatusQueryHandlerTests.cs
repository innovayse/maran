using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Persistence;
using Maran.Modules.Identity.Queries.GetInvitationStatus;
using Maran.Modules.Identity.Tests.TestSupport;
using Maran.SharedKernel.Utilities.Tokens;

namespace Maran.Modules.Identity.Tests.Queries.GetInvitationStatus;

/// <summary>
/// Behavioral contract of <see cref="GetInvitationStatusQueryHandler"/>: where an account owner's
/// invitation stands, computed from the stored token rows at the moment of the request, so a link is
/// only ever offered while there is something to hand over and the page stops offering one the moment
/// the invitation is accepted or has lapsed.
/// </summary>
public sealed class GetInvitationStatusQueryHandlerTests : IDisposable
{
    /// <summary>The hosting account whose owner is invited.</summary>
    private static readonly Guid AccountId = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000001");

    /// <summary>The fixed instant the clock starts at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The module's context, in memory.</summary>
    private readonly IdentityDbContext _context = IdentityTestContext.Create();

    /// <summary>The clock.</summary>
    private readonly FakeClock _clock = new(Now);

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>Asks the handler about the account under test.</summary>
    private async Task<Common.InvitationStatusDto> AskAsync()
    {
        var handler = new GetInvitationStatusQueryHandler(_context, _clock, IdentityTestContext.DisplayNameResources());
        var result = await handler.HandleAsync(new GetInvitationStatusQuery(AccountId), CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    /// <summary>Stores an invited login for the account.</summary>
    private async Task<User> SeedInvitedLoginAsync()
    {
        var user = User.Invite(Guid.NewGuid(), "owner-one", "owner@example.com", AccountId, Now);
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    /// <summary>Stores an invitation token issued at <paramref name="createdAt"/>.</summary>
    /// <param name="user">The login the token invites.</param>
    /// <param name="createdAt">When the token was issued.</param>
    private async Task<InvitationToken> SeedTokenAsync(User user, DateTimeOffset createdAt)
    {
        var token = new InvitationToken(Guid.NewGuid(), user.Id, InvitationTokenHasher.Hash(Guid.NewGuid().ToString()), createdAt);
        _context.InvitationTokens.Add(token);
        await _context.SaveChangesAsync();
        return token;
    }

    /// <summary>An account with no login yet reports no invitation.</summary>
    [Fact]
    public async Task An_account_with_no_login_yet_reports_no_invitation()
    {
        var status = await AskAsync();

        Assert.Equal(InvitationState.None, status.State);
        Assert.Null(status.ExpiresAt);
    }

    /// <summary>An unused unexpired invitation reports pending with its expiry.</summary>
    [Fact]
    public async Task An_unused_unexpired_invitation_reports_pending_with_its_expiry()
    {
        var user = await SeedInvitedLoginAsync();
        var token = await SeedTokenAsync(user, Now);

        var status = await AskAsync();

        Assert.Equal(InvitationState.Pending, status.State);
        Assert.Equal(token.ExpiresAt, status.ExpiresAt);
    }

    /// <summary>An invitation lapses at the instant its token expires, not before.</summary>
    [Fact]
    public async Task An_invitation_lapses_at_the_instant_its_token_expires_and_not_before()
    {
        var user = await SeedInvitedLoginAsync();
        var token = await SeedTokenAsync(user, Now);

        _clock.Advance(InvitationToken.Lifetime - TimeSpan.FromSeconds(1));
        var before = await AskAsync();
        _clock.Advance(TimeSpan.FromSeconds(1));
        var at = await AskAsync();

        Assert.Equal(InvitationState.Pending, before.State);
        Assert.Equal(token.ExpiresAt, _clock.UtcNow);
        Assert.Equal(InvitationState.Expired, at.State);
        Assert.Null(at.ExpiresAt);
    }

    /// <summary>A login whose only token was retired without a replacement reports expired.</summary>
    [Fact]
    public async Task A_login_whose_only_token_was_retired_without_a_replacement_reports_expired()
    {
        var user = await SeedInvitedLoginAsync();
        var token = await SeedTokenAsync(user, Now);
        token.Consume(Now);
        await _context.SaveChangesAsync();

        var status = await AskAsync();

        Assert.Equal(InvitationState.Expired, status.State);
    }

    /// <summary>An invited login that has no token at all reports expired.</summary>
    [Fact]
    public async Task An_invited_login_that_has_no_token_at_all_reports_expired()
    {
        // Nothing is outstanding, so there is nothing to hand over; "expired" is what makes the
        // screen offer a fresh link rather than claim an invitation is on its way.
        await SeedInvitedLoginAsync();

        var status = await AskAsync();

        Assert.Equal(InvitationState.Expired, status.State);
    }

    /// <summary>With several tokens the expiry reported is the usable one's.</summary>
    [Fact]
    public async Task With_several_tokens_the_expiry_reported_is_the_usable_ones()
    {
        var user = await SeedInvitedLoginAsync();
        var retired = await SeedTokenAsync(user, Now - TimeSpan.FromDays(3));
        retired.Consume(Now - TimeSpan.FromDays(2));
        var current = await SeedTokenAsync(user, Now - TimeSpan.FromDays(1));
        await _context.SaveChangesAsync();

        var status = await AskAsync();

        Assert.Equal(InvitationState.Pending, status.State);
        Assert.Equal(current.ExpiresAt, status.ExpiresAt);
    }

    /// <summary>A login that has set its password reports accepted whatever tokens remain.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_login_that_has_set_its_password_reports_accepted_whatever_tokens_remain(bool suspended)
    {
        var user = await SeedInvitedLoginAsync();
        await SeedTokenAsync(user, Now);
        user.Activate("hash");
        if (suspended)
        {
            user.Suspend();
        }

        await _context.SaveChangesAsync();

        var status = await AskAsync();

        Assert.Equal(InvitationState.Accepted, status.State);
        Assert.Null(status.ExpiresAt);
    }

    /// <summary>Another account's invitation is never reported.</summary>
    [Fact]
    public async Task Another_accounts_invitation_is_never_reported()
    {
        var other = User.Invite(Guid.NewGuid(), "other-owner", "other@example.com", Guid.NewGuid(), Now);
        _context.Users.Add(other);
        await _context.SaveChangesAsync();
        await SeedTokenAsync(other, Now);

        var status = await AskAsync();

        Assert.Equal(InvitationState.None, status.State);
    }

    /// <summary>Every state is named for the request's culture, in each language the panel speaks.</summary>
    [Theory]
    [InlineData("en", "Invitation pending")]
    [InlineData("ru", "Приглашение ожидает ответа")]
    [InlineData("hy", "Հրավերը սպասում է պատասխանի")]
    public async Task Every_state_is_named_for_the_requests_culture_in_each_language_the_panel_speaks(
        string culture,
        string expectedPending)
    {
        var user = await SeedInvitedLoginAsync();
        await SeedTokenAsync(user, Now);
        var previous = System.Globalization.CultureInfo.CurrentUICulture;
        try
        {
            System.Globalization.CultureInfo.CurrentUICulture = new System.Globalization.CultureInfo(culture);

            var status = await AskAsync();

            Assert.Equal(expectedPending, status.StateDisplayName);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentUICulture = previous;
        }
    }

    /// <summary>The four states carry four different names, none of them the raw resource key.</summary>
    [Fact]
    public async Task The_four_states_carry_four_different_names_none_of_them_the_raw_resource_key()
    {
        var names = new List<string>();
        names.Add((await AskAsync()).StateDisplayName);
        var user = await SeedInvitedLoginAsync();
        names.Add((await AskAsync()).StateDisplayName);
        await SeedTokenAsync(user, Now);
        names.Add((await AskAsync()).StateDisplayName);
        user.Activate("hash");
        await _context.SaveChangesAsync();
        names.Add((await AskAsync()).StateDisplayName);

        Assert.Equal(4, names.Distinct().Count());
        Assert.All(names, name =>
        {
            Assert.DoesNotContain("InvitationState", name, StringComparison.Ordinal);
        });
    }
}
