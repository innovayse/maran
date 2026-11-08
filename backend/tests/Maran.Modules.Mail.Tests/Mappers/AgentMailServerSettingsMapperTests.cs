using Maran.Modules.Mail.Mappers;
using Maran.Modules.Mail.Tests.TestSupport;

namespace Maran.Modules.Mail.Tests.Mappers;

/// <summary>Behaviour of the mapper that restates server settings in the agent's call shape.</summary>
public sealed class AgentMailServerSettingsMapperTests
{
    /// <summary>Every setting arrives in the agent's call unchanged.</summary>
    [Fact]
    public void Every_setting_arrives_in_the_agent_call_unchanged()
    {
        var command = MailServerCommands.Enable(
            pop3: true, antivirus: true, external: true, sizeMib: 80, greylist: 2, header: 7, reject: 30);

        var sent = AgentMailServerSettingsMapper.ToAgent(command, "mx.example.org");

        Assert.Equal("mx.example.org", sent.Hostname);
        Assert.True(sent.Pop3Enabled);
        Assert.True(sent.AntivirusEnabled);
        Assert.True(sent.AllowExternalForwarding);
        Assert.Equal(80u, sent.MessageSizeMib);
        Assert.Equal(2u, sent.SpamGreylistScore);
        Assert.Equal(7u, sent.SpamAddHeaderScore);
        Assert.Equal(30u, sent.SpamRejectScore);
    }

    /// <summary>The webmail master hash is never part of a server settings call.</summary>
    [Fact]
    public void The_webmail_master_hash_is_never_part_of_the_call()
    {
        var sent = AgentMailServerSettingsMapper.ToAgent(MailServerCommands.Enable(), "mail.example.com");

        Assert.Null(sent.WebmailMasterHash);
    }
}
