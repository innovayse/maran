namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// The error codes the server administration operations raise, spelled once for the tests that
/// assert them.
/// </summary>
/// <remarks>
/// The module's own code says <c>nameof(ErrorMessages.X)</c>, but that generated class is
/// <c>internal</c> and a test project cannot name it. The spelled strings are the resx keys, so a
/// drift would show as a resource census failure and not pass silently.
/// </remarks>
public static class MailServerErrorCodes
{
    /// <summary>Mail is not enabled on this server.</summary>
    public const string MailNotEnabled = "MailNotEnabled";

    /// <summary>The server host name is not a well-formed DNS name.</summary>
    public const string MailServerHostnameInvalidFormat = "MailServerHostnameInvalidFormat";

    /// <summary>The server host name is longer than a DNS name may be.</summary>
    public const string MailServerHostnameTooLong = "MailServerHostnameTooLong";

    /// <summary>The server host name is in the reserved set.</summary>
    public const string MailServerHostnameReserved = "MailServerHostnameReserved";

    /// <summary>The message size limit is outside the allowed range.</summary>
    public const string MailMessageSizeOutOfRange = "MailMessageSizeOutOfRange";

    /// <summary>The spam thresholds are out of range or not strictly increasing.</summary>
    public const string MailSpamScoresInvalid = "MailSpamScoresInvalid";

    /// <summary>The queue id is not shaped like a queue id.</summary>
    public const string MailQueueIdInvalid = "MailQueueIdInvalid";

    /// <summary>No such message is queued.</summary>
    public const string MailQueueEntryNotFound = "MailQueueEntryNotFound";
}
