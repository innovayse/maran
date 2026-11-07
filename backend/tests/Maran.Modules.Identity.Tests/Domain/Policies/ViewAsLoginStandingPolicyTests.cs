using Maran.Modules.Identity.Domain.Entities;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioural contract of <see cref="ViewAsLoginStandingPolicy"/> (issue #416): a login that has not
/// accepted its invitation is told apart from a suspended one, and a suspension wins when both hold.
/// </summary>
public sealed class ViewAsLoginStandingPolicyTests
{
    /// <summary>The instant every login here is created at.</summary>
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A customer login that holds a password.</summary>
    /// <returns>The login.</returns>
    private static User ActiveCustomer()
    {
        return new User(Guid.NewGuid(), "olive", "olive@example.com", "hash", UserRole.Customer, Now);
    }

    /// <summary>A reseller login that has not accepted its invitation.</summary>
    /// <returns>The login.</returns>
    private static User InvitedReseller()
    {
        return User.InviteReseller(Guid.NewGuid(), "nova", "nova@example.com", Guid.NewGuid(), Now);
    }

    /// <summary>A claimed login with an allowed membership can be viewed as.</summary>
    [Fact]
    public void A_claimed_login_with_an_allowed_membership_is_available()
    {
        Assert.Equal(ViewAsLoginStanding.Available, ViewAsLoginStandingPolicy.Of(ActiveCustomer(), membershipSuspended: false));
    }

    /// <summary>A claimed login whose membership of the viewed account is suspended is unavailable.</summary>
    [Fact]
    public void A_suspended_membership_is_unavailable()
    {
        Assert.Equal(ViewAsLoginStanding.Unavailable, ViewAsLoginStandingPolicy.Of(ActiveCustomer(), membershipSuspended: true));
    }

    /// <summary>A login whose owner has not accepted the invitation is invitation pending, not unavailable.</summary>
    [Fact]
    public void An_invited_login_is_invitation_pending()
    {
        var invited = User.Invite(Guid.NewGuid(), "waiting", "waiting@example.com", Now);

        Assert.Equal(ViewAsLoginStanding.InvitationPending, ViewAsLoginStandingPolicy.Of(invited, membershipSuspended: false));
    }

    /// <summary>An invited login whose membership is suspended is unavailable: the suspension is what to resolve first.</summary>
    [Fact]
    public void An_invited_login_with_a_suspended_membership_is_unavailable()
    {
        var invited = User.Invite(Guid.NewGuid(), "waiting", "waiting@example.com", Now);

        Assert.Equal(ViewAsLoginStanding.Unavailable, ViewAsLoginStandingPolicy.Of(invited, membershipSuspended: true));
    }

    /// <summary>A suspended reseller login that is still invited is unavailable, and resuming it makes it pending.</summary>
    [Fact]
    public void A_suspended_invited_reseller_login_is_unavailable_until_resumed()
    {
        var reseller = InvitedReseller();
        reseller.Suspend();
        Assert.Equal(ViewAsLoginStanding.Unavailable, ViewAsLoginStandingPolicy.Of(reseller, membershipSuspended: false));

        reseller.Resume();
        Assert.Equal(ViewAsLoginStanding.InvitationPending, ViewAsLoginStandingPolicy.Of(reseller, membershipSuspended: false));
    }

    /// <summary>A suspended reseller login that accepted its invitation is unavailable.</summary>
    [Fact]
    public void A_suspended_claimed_reseller_login_is_unavailable()
    {
        var reseller = InvitedReseller();
        reseller.Activate("hash");
        reseller.Suspend();

        Assert.Equal(ViewAsLoginStanding.Unavailable, ViewAsLoginStandingPolicy.Of(reseller, membershipSuspended: false));
    }

    /// <summary>A missing login is a defect in the caller, not a quiet refusal.</summary>
    [Fact]
    public void A_null_login_throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            ViewAsLoginStandingPolicy.Of(null!, membershipSuspended: false);
        });
    }
}
