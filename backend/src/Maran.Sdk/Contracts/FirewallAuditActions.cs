namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for firewall rule, ban and whitelist actions. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class FirewallAuditActions
{
    /// <summary>A port was opened on the host firewall, optionally scoped to a source range.</summary>
    public const string FirewallRuleAllowed = "FirewallRuleAllowed";

    /// <summary>A port was closed on the host firewall.</summary>
    public const string FirewallRuleDenied = "FirewallRuleDenied";

    /// <summary>
    /// An address was banned from the host. Recorded whether a human or the brute-force detector
    /// asked for it, because "who banned this customer's office" is the question an operator will
    /// have, and an automatic ban with no entry is indistinguishable from a network fault.
    /// </summary>
    public const string AddressBanned = "AddressBanned";

    /// <summary>A ban was lifted before its timeout, or a permanent one was removed.</summary>
    public const string AddressUnbanned = "AddressUnbanned";

    /// <summary>The firewall whitelist was changed. Addresses on it are never banned automatically.</summary>
    public const string FirewallWhitelistChanged = "FirewallWhitelistChanged";

    /// <summary>
    /// The installer's recorded address became the whitelist's first row, at the panel's own
    /// initiative and with nobody signed in. Recorded because it is an exemption from every
    /// automatic ban that no request created, so without this entry the one row an operator never
    /// added is also the one row with no history.
    /// </summary>
    public const string FirewallWhitelistSeeded = "FirewallWhitelistSeeded";

    /// <summary>
    /// A brute-force ban was NOT applied because the address is whitelisted. Recorded as its own
    /// action rather than as a failure: nothing went wrong, and the absence of a ban an operator
    /// expected is exactly what this explains.
    /// </summary>
    public const string BanSkippedWhitelisted = "BanSkippedWhitelisted";
}
