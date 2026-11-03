using System.Diagnostics;
using Maran.Modules.Identity.Domain.Enums;
using Maran.Modules.Identity.Domain.Policies;

namespace Maran.Modules.Identity.Tests.Domain.Policies;

/// <summary>
/// Behavioural contract of <see cref="ViewAsKindPolicy"/> (issues #317, #179): which logins can be
/// viewed as, and which sets of facts describe a view-as session consistently.
/// </summary>
public sealed class ViewAsKindPolicyTests
{
    private static readonly Guid ResellerId = new("aaaaaaaa-0000-4000-8000-0000000000e1");

    private static readonly Guid AccountId = new("aaaaaaaa-0000-4000-8000-0000000000a1");

    /// <summary>A customer login is viewed as an account and a reseller login as a reseller.</summary>
    [Theory]
    [InlineData(UserRole.Customer, ViewAsKind.Account)]
    [InlineData(UserRole.Reseller, ViewAsKind.Reseller)]
    public void A_customer_and_a_reseller_login_each_have_their_kind(UserRole role, ViewAsKind expected)
    {
        Assert.True(ViewAsKindPolicy.TryKindOf(role, out var kind));
        Assert.Equal(expected, kind);
    }

    /// <summary>An administrator is never a target: no kind is offered for the role.</summary>
    [Fact]
    public void An_administrator_login_has_no_kind()
    {
        Assert.False(ViewAsKindPolicy.TryKindOf(UserRole.Admin, out _));
    }

    /// <summary>A role the policy has never heard of is a defect, not a quiet "no".</summary>
    [Fact]
    public void An_undefined_role_throws_rather_than_being_refused_quietly()
    {
        Assert.Throws<UnreachableException>(() =>
        {
            ViewAsKindPolicy.TryKindOf((UserRole)99, out _);
        });
    }

    /// <summary>Every role, reseller-id and active-account combination is decided as the rule states.</summary>
    /// <remarks>
    /// The whole grid is walked, not sampled: three roles by two reseller-id states by two account
    /// states. The expected subject is the account for a customer, the reseller for a reseller, and
    /// nothing for a refused combination, so a policy that returned the wrong id of the two fails
    /// the accepting rows as well as the refusing ones.
    /// </remarks>
    /// <param name="role">The viewed login's role.</param>
    /// <param name="hasResellerId">Whether the login carries a reseller id.</param>
    /// <param name="hasAccount">Whether the session has an active account.</param>
    /// <param name="expectedKind">The kind when accepted, null when refused.</param>
    /// <param name="expectedSubject">Which id is the subject: account, reseller, or none.</param>
    [Theory]
    [InlineData(UserRole.Customer, false, true, ViewAsKind.Account, "account")]
    [InlineData(UserRole.Customer, true, true, ViewAsKind.Account, "account")]
    [InlineData(UserRole.Customer, false, false, null, "none")]
    [InlineData(UserRole.Customer, true, false, null, "none")]
    [InlineData(UserRole.Reseller, true, false, ViewAsKind.Reseller, "reseller")]
    [InlineData(UserRole.Reseller, true, true, null, "none")]
    [InlineData(UserRole.Reseller, false, false, null, "none")]
    [InlineData(UserRole.Reseller, false, true, null, "none")]
    [InlineData(UserRole.Admin, false, false, null, "none")]
    [InlineData(UserRole.Admin, true, false, null, "none")]
    [InlineData(UserRole.Admin, false, true, null, "none")]
    [InlineData(UserRole.Admin, true, true, null, "none")]
    public void Every_combination_of_facts_is_decided_as_the_rule_states(
        UserRole role,
        bool hasResellerId,
        bool hasAccount,
        ViewAsKind? expectedKind,
        string expectedSubject)
    {
        var accepted = ViewAsKindPolicy.TryDescribe(
            role,
            hasResellerId ? ResellerId : null,
            hasAccount ? AccountId : null,
            out var kind,
            out var subjectId);

        Assert.Equal(expectedKind is not null, accepted);
        if (expectedKind is { } expected)
        {
            Assert.Equal(expected, kind);
        }

        Assert.Equal(
            expectedSubject switch
            {
                "account" => AccountId,
                "reseller" => ResellerId,
                _ => Guid.Empty,
            },
            subjectId);
    }
}
