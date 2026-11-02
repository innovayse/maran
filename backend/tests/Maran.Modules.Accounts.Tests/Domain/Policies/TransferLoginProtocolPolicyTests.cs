using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="TransferLoginProtocolPolicy"/>: how the account lifecycle reads
/// and words a list of logins that holds two daemons' credentials and used to hold one. Every
/// login in it names its daemon: the client resolves an unstated one at the boundary.
/// </summary>
public sealed class TransferLoginProtocolPolicyTests
{
    /// <summary>A named login carries the daemon that serves it.</summary>
    [Theory]
    [InlineData(LoginTransferProtocol.Sftp, "acme_web (sftp)")]
    [InlineData(LoginTransferProtocol.Ftps, "acme_web (ftps)")]
    public void A_named_login_carries_the_daemon_that_serves_it(LoginTransferProtocol protocol, string expected)
    {
        // The whole sentence, not a substring: "acme_web" alone matches both rows and the bare name
        // as well, so a check for it would pass over every wrong answer this method can give.
        var described = TransferLoginProtocolPolicy.Describe(
            new FileTransferLoginSuspensionFactDto("acme_web", false, protocol));

        Assert.Equal(expected, described);
    }

    /// <summary>A list holding both daemons is worded as two counts.</summary>
    [Fact]
    public void A_list_holding_both_daemons_is_worded_as_two_counts()
    {
        // Three and two, not two and three: a phrase that swapped the counts reads identically to a
        // careless eye and is the exact error this wording exists to prevent.
        var described = TransferLoginProtocolPolicy.DescribeAll([
            new FileTransferLoginSuspensionFactDto("acme_a", true, LoginTransferProtocol.Sftp),
            new FileTransferLoginSuspensionFactDto("acme_b", true, LoginTransferProtocol.Sftp),
            new FileTransferLoginSuspensionFactDto("acme_c", true, LoginTransferProtocol.Sftp),
            new FileTransferLoginSuspensionFactDto("acme_d", true, LoginTransferProtocol.Ftps),
            new FileTransferLoginSuspensionFactDto("acme_e", true, LoginTransferProtocol.Ftps),
        ]);

        Assert.Equal("all 3 of its sftp logins and all 2 of its ftps logins", described);
    }

    /// <summary>A list holding only ftps logins is never worded as sftp ones.</summary>
    [Fact]
    public void A_list_holding_only_ftps_logins_is_never_worded_as_sftp_ones()
    {
        // The defect this type was written for, in its purest form: the panel counted a list of FTPS
        // credentials and told the operator its SFTP logins had been dealt with.
        var described = TransferLoginProtocolPolicy.DescribeAll([
            new FileTransferLoginSuspensionFactDto("acme_d", true, LoginTransferProtocol.Ftps),
            new FileTransferLoginSuspensionFactDto("acme_e", true, LoginTransferProtocol.Ftps),
        ]);

        Assert.Equal("all 0 of its sftp logins and all 2 of its ftps logins", described);
    }

    /// <summary>An account with no login of either kind names both daemons and not one of them.</summary>
    [Fact]
    public void An_account_with_no_login_of_either_kind_names_both_daemons_and_not_one_of_them()
    {
        // The live defect, in the case it was read in. This phrase used to be "all 0 of its sftp
        // logins": one daemon of two named over a suspension that locks both, which an operator reads
        // as "FTPS was not in scope". Asserted as the whole phrase, because "all 0 of its sftp logins"
        // is a substring of the correct answer and a check for it would pass over the defect.
        var described = TransferLoginProtocolPolicy.DescribeAll([]);

        Assert.Equal("all 0 of its sftp logins and all 0 of its ftps logins", described);
    }
}
