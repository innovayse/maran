using Maran.Modules.Mail.Commands.EnableMailServer;
using Maran.Modules.Mail.Commands.UpdateMailServerSettings;

namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>Builds the server administration commands the tests send, with valid defaults a test overrides one at a time.</summary>
public static class MailServerCommands
{
    /// <summary>The host name the commands carry unless a test says otherwise.</summary>
    public const string Hostname = "mail.example.com";

    /// <summary>The caller's address the commands carry.</summary>
    public const string Address = "203.0.113.9";

    /// <summary>The caller's client the commands carry.</summary>
    public const string Client = "tests";

    /// <summary>Builds an enable command; every argument has a valid default.</summary>
    /// <param name="hostname">The host name.</param>
    /// <param name="pop3">Whether POP3 is served.</param>
    /// <param name="antivirus">Whether virus scanning is on.</param>
    /// <param name="external">Whether external forwarding is allowed.</param>
    /// <param name="sizeMib">The message size limit.</param>
    /// <param name="greylist">The greylist threshold.</param>
    /// <param name="header">The add-header threshold.</param>
    /// <param name="reject">The reject threshold.</param>
    /// <returns>The command.</returns>
    public static EnableMailServerCommand Enable(
        string hostname = Hostname,
        bool pop3 = false,
        bool antivirus = false,
        bool external = false,
        int sizeMib = 50,
        int greylist = 4,
        int header = 6,
        int reject = 15)
    {
        return new EnableMailServerCommand(
            hostname, pop3, antivirus, external, sizeMib, greylist, header, reject, Address, Client);
    }

    /// <summary>Builds an update command; every argument has a valid default.</summary>
    /// <param name="hostname">The host name.</param>
    /// <param name="pop3">Whether POP3 is served.</param>
    /// <param name="antivirus">Whether virus scanning is on.</param>
    /// <param name="external">Whether external forwarding is allowed.</param>
    /// <param name="sizeMib">The message size limit.</param>
    /// <param name="greylist">The greylist threshold.</param>
    /// <param name="header">The add-header threshold.</param>
    /// <param name="reject">The reject threshold.</param>
    /// <returns>The command.</returns>
    public static UpdateMailServerSettingsCommand Update(
        string hostname = Hostname,
        bool pop3 = false,
        bool antivirus = false,
        bool external = false,
        int sizeMib = 50,
        int greylist = 4,
        int header = 6,
        int reject = 15)
    {
        return new UpdateMailServerSettingsCommand(
            hostname, pop3, antivirus, external, sizeMib, greylist, header, reject, Address, Client);
    }
}
