using Maran.Host.Demo.Guard;
using Maran.Host.Demo.Guard.Enums;

namespace Maran.Host.Demo.Tests.Guard;

/// <summary>Properties of the real table that do not need the composed controllers.</summary>
/// <remarks>
/// That the table names every mutating action is <c>DemoRefusalCensusTests</c> in the architecture suite.
/// </remarks>
public sealed class DemoEndpointTableTests
{
    /// <summary>The areas the SPA knows by these exact ids.</summary>
    private static readonly string[] SpaAreaIds =
    [
        "securityPolicy", "smtp", "panelCertificate", "panelUpdate", "twoFactor", "apiKeys", "licence",
    ];

    /// <summary>The table is not empty (the walk over it would otherwise pass over nothing).</summary>
    [Fact]
    public void The_table_names_a_hundred_or_more_actions()
    {
        Assert.True(DemoEndpointTable.Rules.Count >= 100, $"Only {DemoEndpointTable.Rules.Count} actions are named.");
    }

    /// <summary>Every refusal names an area and every area is spelled once.</summary>
    [Fact]
    public void Every_refusal_names_an_area_and_every_area_has_its_own_id()
    {
        var refusals = DemoEndpointTable.Rules.Values.Where(
            rule => { return rule.Access == DemoAccess.Refused; }).ToList();
        var ids = DemoRefusalAreaIds.All.Select(DemoRefusalAreaIds.IdOf).ToList();

        Assert.NotEmpty(refusals);
        Assert.All(refusals, rule => { Assert.NotNull(rule.Area); });
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>The ids the SPA keys its reasons on are exactly the ones the guard publishes.</summary>
    [Fact]
    public void The_areas_the_spa_knows_are_spelled_exactly_as_the_spa_spells_them()
    {
        var ids = DemoRefusalAreaIds.All.Select(DemoRefusalAreaIds.IdOf).ToList();

        Assert.All(SpaAreaIds, id => { Assert.Contains(id, ids); });
    }

    /// <summary>The actions that make the shared logins unusable stay refused whatever else changes.</summary>
    [Theory]
    [InlineData("AuthController.ChangePasswordAsync")]
    [InlineData("AuthController.LogoutEverywhereAsync")]
    [InlineData("AuthController.DisableTwoFactorAsync")]
    [InlineData("AuthController.BeginTwoFactorEnrolmentAsync")]
    [InlineData("AuthController.ConfirmTwoFactorEnrolmentAsync")]
    [InlineData("AuthController.ResetPasswordAsync")]
    [InlineData("AuthController.RequestPasswordResetAsync")]
    [InlineData("AuthController.AcceptInvitationAsync")]
    [InlineData("SessionsController.RevokeAsync")]
    [InlineData("SecurityPolicyController.SaveAsync")]
    [InlineData("AuditRetentionController.SaveAsync")]
    [InlineData("SmtpSettingsController.SaveAsync")]
    [InlineData("SmtpSettingsController.SendTestAsync")]
    [InlineData("LicensingController.InstallAsync")]
    [InlineData("PanelCertificateController.IssueAsync")]
    [InlineData("ApiKeysController.CreateAsync")]
    [InlineData("SubUsersController.InviteAsync")]
    [InlineData("InvitationsController.IssueLinkAsync")]
    [InlineData("FirewallWhitelistController.CreateAsync")]
    [InlineData("SetupController.CompleteAsync")]
    public void The_credential_and_policy_actions_stay_refused(string action)
    {
        Assert.Equal(DemoAccess.Refused, DemoEndpointTable.Rules[action].Access);
    }

    /// <summary>The ones that can disable a seeded login's account or plan check the protected set.</summary>
    [Theory]
    [InlineData("AccountsController.SuspendAsync")]
    [InlineData("AccountsController.DeleteAsync")]
    [InlineData("AccountsController.TransferAsync")]
    [InlineData("AccountsController.ChangePlanAsync")]
    [InlineData("PlansController.DeleteAsync")]
    [InlineData("ResellersController.SuspendAsync")]
    [InlineData("ResellersController.DeleteAsync")]
    public void The_actions_that_could_lock_a_seeded_login_out_check_the_protected_set(string action)
    {
        Assert.Equal(DemoAccess.AllowedUnlessProtected, DemoEndpointTable.Rules[action].Access);
    }

    /// <summary>Sign-in, refresh and sign-out draw on their own allowances and never on the mutation budgets.</summary>
    [Theory]
    [InlineData("AuthController.LoginAsync")]
    [InlineData("AuthController.RefreshAsync")]
    [InlineData("AuthController.LogoutAsync")]
    public void Getting_in_and_out_draws_on_its_own_allowance_only(string action)
    {
        var rule = DemoEndpointTable.Rules[action];

        Assert.Equal(DemoAccess.Allowed, rule.Access);
        Assert.False(rule.CountsTowardBudget);
        Assert.True(rule.CountsTowardSignInBudget);
    }
}
