using Maran.Agent.Client.Services.AccountsService;
using Maran.Modules.Accounts.Domain.Policies;

namespace Maran.Modules.Accounts.Tests.Domain.Policies;

/// <summary>
/// Behavioral contract of <see cref="TransferLoginProtocolPolicy"/>: how the account lifecycle reads
/// and words a list of logins that holds two daemons' credentials and used to hold one.
/// </summary>
public sealed class TransferLoginProtocolPolicyTests
{
    /// <summary>A login the host said nothing about reads as sftp.</summary>
    [Fact]
    public void A_login_the_host_said_nothing_about_reads_as_sftp()
    {
        // Not a convenience default: before FTPS existed every fact in this list was an SFTP login by
        // construction, so an agent's silence has exactly one correct reading.
        var reading = TransferLoginProtocolPolicy.Read(new FileTransferLoginSuspensionFactDto("acme_web", true));

        Assert.Equal(LoginTransferProtocol.Sftp, reading);
    }

    /// <summary>A login the host called ftps reads as ftps.</summary>
    [Fact]
    public void A_login_the_host_called_ftps_reads_as_ftps()
    {
        // The inverse control for the reading above: a policy that answered Sftp for everything would
        // satisfy that test and fail this one.
        var reading = TransferLoginProtocolPolicy.Read(
            new FileTransferLoginSuspensionFactDto("acme_web", true, LoginTransferProtocol.Ftps));

        Assert.Equal(LoginTransferProtocol.Ftps, reading);
    }

    /// <summary>A named login carries the daemon the host stated.</summary>
    [Theory]
    [InlineData(LoginTransferProtocol.Sftp, "acme_web (sftp)")]
    [InlineData(LoginTransferProtocol.Ftps, "acme_web (ftps)")]
    public void A_named_login_carries_the_daemon_the_host_stated(LoginTransferProtocol protocol, string expected)
    {
        // The whole sentence, not a substring: "acme_web" alone matches both rows and the bare name
        // as well, so a check for it would pass over every wrong answer this method can give.
        var described = TransferLoginProtocolPolicy.Describe(
            new FileTransferLoginSuspensionFactDto("acme_web", false, protocol));

        Assert.Equal(expected, described);
    }

    /// <summary>A named login the host said nothing about carries no daemon.</summary>
    [Fact]
    public void A_named_login_the_host_said_nothing_about_carries_no_daemon()
    {
        // The counting rule reads this login as SFTP, and the naming rule deliberately does not:
        // printing "(sftp)" beside a name would put on the operator's screen a fact the host never
        // stated, on the one line whose job is to send them to the right client.
        var described = TransferLoginProtocolPolicy.Describe(new FileTransferLoginSuspensionFactDto("acme_web", false));

        Assert.Equal("acme_web", described);
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

    /// <summary>A list from an agent that names no daemon is counted under sftp and still names ftps.</summary>
    [Fact]
    public void A_list_from_an_agent_that_names_no_daemon_is_counted_under_sftp_and_still_names_ftps()
    {
        // The reading rule observed through the wording: two unstated logins are counted as SFTP. The
        // other daemon is still named at zero, because an agent too old to state a protocol is not
        // evidence that the host runs no FTPS.
        var described = TransferLoginProtocolPolicy.DescribeAll([
            new FileTransferLoginSuspensionFactDto("acme_a", true),
            new FileTransferLoginSuspensionFactDto("acme_b", true),
        ]);

        Assert.Equal("all 2 of its sftp logins and all 0 of its ftps logins", described);
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
