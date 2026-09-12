namespace Maran.Modules.Ftp.Tests.TestSupport;

/// <summary>
/// The error codes this module raises, spelled once for the tests that assert them.
/// </summary>
/// <remarks>
/// <para>
/// The module's own code says <c>nameof(ErrorMessages.X)</c>, where <c>ErrorMessages</c> is the
/// class MSBuild generates from the neutral resx — and that class is <c>internal</c>, exactly as it
/// is in every shipped module, so a test project cannot name it. Making it public for the tests'
/// convenience would give this one module a public resource class no other module has, which is a
/// worse trade than spelling four strings.
/// </para>
/// <para>
/// The obvious hazard of a spelled string is that it drifts from the resx and the assertions go on
/// passing against a code the panel no longer raises. That is closed by
/// <c>ErrorMessagesTests.Every_code_this_module_raises_has_an_entry_in_all_three_locales</c>, which
/// checks every constant here against the three resx files — a stronger guarantee than
/// <c>nameof</c> gave, because <c>nameof</c> proved the key existed in the NEUTRAL file only.
/// </para>
/// </remarks>
public static class ErrorCodes
{
    /// <summary>The panel serves no site for the host name FTPS was asked to serve.</summary>
    public const string FtpsHostnameNotServed = "FtpsHostnameNotServed";

    /// <summary>No TLS certificate material exists for the host name, so the daemon cannot start.</summary>
    public const string FtpsCertificateMissing = "FtpsCertificateMissing";

    /// <summary>The host name is not a well-formed DNS name.</summary>
    public const string FtpsHostnameInvalidFormat = "FtpsHostnameInvalidFormat";

    /// <summary>The host name is longer than DNS allows.</summary>
    public const string FtpsHostnameTooLong = "FtpsHostnameTooLong";

    /// <summary>The PASV address is not a literal IPv4 address.</summary>
    public const string FtpsPassiveAddressInvalid = "FtpsPassiveAddressInvalid";

    /// <summary>No login of the caller's answers to the identifier they named.</summary>
    /// <remarks>
    /// The one answer for "there is no such login" AND for "the login belongs to somebody else". A
    /// distinct code for the second would confirm a neighbouring tenant's login exists.
    /// </remarks>
    public const string FtpUserNotFound = "FtpUserNotFound";

    /// <summary>The login name is already held — by this account's rows, or by the host itself.</summary>
    public const string FtpUserNameTaken = "FtpUserNameTaken";

    /// <summary>The account's plan allows no further FTPS logins.</summary>
    public const string FtpUserLimitReached = "FtpUserLimitReached";

    /// <summary>
    /// The account's plan filled up while this login was being created, so the login that was made on
    /// the host has been removed again.
    /// </summary>
    public const string FtpUserLimitReachedConcurrently = "FtpUserLimitReachedConcurrently";

    /// <summary>The name overflows the host's user-name ceiling once the account prefix is added.</summary>
    public const string FtpUserNameTooLong = "FtpUserNameTooLong";

    /// <summary>The login name is not lowercase letters and digits.</summary>
    public const string FtpUserNameInvalidFormat = "FtpUserNameInvalidFormat";

    /// <summary>The login reached the host but could not be recorded, and was undone.</summary>
    public const string FtpUserProvisioningFailed = "FtpUserProvisioningFailed";

    /// <summary>No account of the caller's answers to the identifier they named.</summary>
    public const string AccountNotFound = "AccountNotFound";
}
