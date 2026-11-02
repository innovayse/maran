using Maran.Agent.Client.Services.AccountsService;
using Maran.Agent.Client.Services.MonitorService;
using Maran.Modules.Monitoring.Mappers;

namespace Maran.Modules.Monitoring.Tests.Mappers;

/// <summary>What an alert mail's detail line says for each case of a finding; it never invents a reason.</summary>
public sealed class AlertDetailMapperTests
{
    /// <summary>Each reason a filesystem cannot enforce a quota is named in its own words.</summary>
    /// <param name="reason">The agent's reason.</param>
    /// <param name="expected">The phrase the mail carries.</param>
    [Theory]
    [InlineData(QuotaUnenforceableReason.MountedWithoutQuotaAccounting, "not mounted with quota accounting")]
    [InlineData(QuotaUnenforceableReason.AccountingNotEnabled, "quota accounting is off")]
    public void A_not_enforceable_finding_is_rendered_with_its_own_reason(
        QuotaUnenforceableReason reason,
        string expected)
    {
        var text = AlertDetailMapper.FormatReason(new AgentQuotaEnforceability.NotEnforceable(reason));

        Assert.Equal(expected, text);
    }

    /// <summary>An enforceable filesystem has no reason, so the detail is empty rather than naming one.</summary>
    [Fact]
    public void An_enforceable_finding_has_no_reason_to_render()
    {
        var text = AlertDetailMapper.FormatReason(new AgentQuotaEnforceability.Enforceable());

        Assert.Equal(string.Empty, text);
    }

    /// <summary>A drifted jail block lists what the agent found missing.</summary>
    [Fact]
    public void A_drifted_jail_block_lists_what_is_missing()
    {
        var text = AlertDetailMapper.FormatMissing(
            new AgentSftpJailStatus.Drifted(["forcecommand internal-sftp", "x11forwarding no"]));

        Assert.Equal("forcecommand internal-sftp, x11forwarding no", text);
    }

    /// <summary>An intact jail block has nothing missing, and the detail is empty.</summary>
    [Fact]
    public void An_intact_jail_block_has_nothing_to_list()
    {
        var text = AlertDetailMapper.FormatMissing(new AgentSftpJailStatus.Intact());

        Assert.Equal(string.Empty, text);
    }
}
