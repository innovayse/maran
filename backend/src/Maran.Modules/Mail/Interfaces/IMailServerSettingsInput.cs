namespace Maran.Modules.Mail.Interfaces;

/// <summary>
/// The settings an administrator states for the mail server, shared by the command that turns the
/// server on and the one that re-states them on a server that is already on, so that the rules which
/// judge them and the translation that sends them to the agent are written once.
/// </summary>
public interface IMailServerSettingsInput
{
    /// <summary>The host name the server presents and its certificate is for.</summary>
    string Hostname { get; }

    /// <summary>Whether POP3 and POP3S are served beside IMAP.</summary>
    bool Pop3Enabled { get; }

    /// <summary>Whether incoming mail is scanned for viruses.</summary>
    bool AntivirusEnabled { get; }

    /// <summary>Whether a forwarder may point at an address outside the owning account's own mail domains.</summary>
    bool AllowExternalForwarding { get; }

    /// <summary>The largest message the server accepts, in mebibytes.</summary>
    int MaxMessageMib { get; }

    /// <summary>The spam score from which a message is greylisted.</summary>
    int SpamGreylistScore { get; }

    /// <summary>The spam score from which a spam header is added.</summary>
    int SpamAddHeaderScore { get; }

    /// <summary>The spam score from which a message is rejected.</summary>
    int SpamRejectScore { get; }
}
