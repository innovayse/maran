namespace Maran.Modules.Mail.Tests.TestSupport;

/// <summary>
/// The error codes the alias operations raise, spelled once for the tests that assert them.
/// </summary>
/// <remarks>
/// The module's own code says <c>nameof(ErrorMessages.X)</c>, and that class is <c>internal</c> as it is in
/// every shipped module, so a test project cannot name it. The spelled strings are held to the resx by the
/// module's resource-table tests, which check every code the module raises against all three locales.
/// </remarks>
public static class MailAliasErrorCodes
{
    /// <summary>The account is unknown or not visible to the caller.</summary>
    public const string AccountNotFound = "AccountNotFound";

    /// <summary>An administrator has not turned mail on.</summary>
    public const string MailNotEnabled = "MailNotEnabled";

    /// <summary>No such domain is visible to the caller.</summary>
    public const string MailDomainNotFound = "MailDomainNotFound";

    /// <summary>No such address is visible to the caller.</summary>
    public const string MailAliasNotFound = "MailAliasNotFound";

    /// <summary>The address is already an alias or a mailbox.</summary>
    public const string MailAliasTaken = "MailAliasTaken";

    /// <summary>The target list is empty, holds a refused address, or repeats one.</summary>
    public const string MailAliasTargetInvalid = "MailAliasTargetInvalid";

    /// <summary>The target list is longer than the ceiling.</summary>
    public const string MailAliasTargetLimit = "MailAliasTargetLimit";

    /// <summary>The stated kind is not what the targets make.</summary>
    public const string MailAliasKindMismatch = "MailAliasKindMismatch";

    /// <summary>A target leaves the account and the host forbids it.</summary>
    public const string MailAliasExternalTargetNotAllowed = "MailAliasExternalTargetNotAllowed";

    /// <summary>The domain already has a catch-all.</summary>
    public const string MailCatchAllConflict = "MailCatchAllConflict";

    /// <summary>The address could not be written after the host was changed.</summary>
    public const string MailAliasProvisioningFailed = "MailAliasProvisioningFailed";

    /// <summary>The local part is not a valid mailbox grammar.</summary>
    public const string MailboxLocalPartInvalid = "MailboxLocalPartInvalid";
}
