namespace Maran.Sdk.Contracts;

/// <summary>
/// The machine-stable audit action names for sign-in, second factor, password, invitation, sub-user, pinned-page and view-as actions: everything that is about a person and their access to the panel. Constants rather than an enum: a marketplace
/// module records actions this assembly was never compiled knowing about, and an enum could not be
/// extended from outside.
/// </summary>
/// <remarks>
/// One of the Sdk's <c>*AuditActions</c> classes, which together are the closed set of actions the
/// shipped modules record; each is named for the area whose journal writes its constants.
/// </remarks>
public static class IdentityAuditActions
{
    /// <summary>A user signed in.</summary>
    public const string LoginSucceeded = "LoginSucceeded";

    /// <summary>A sign-in attempt was refused.</summary>
    public const string LoginFailed = "LoginFailed";

    /// <summary>A user signed out of one device.</summary>
    public const string LoggedOut = "LoggedOut";

    /// <summary>A user signed out of every device.</summary>
    public const string LoggedOutEverywhere = "LoggedOutEverywhere";

    /// <summary>A session was ended from the sessions screen.</summary>
    public const string SessionRevoked = "SessionRevoked";

    /// <summary>A refresh token was presented after it had already been rotated.</summary>
    public const string RefreshTokenReuseDetected = "RefreshTokenReuseDetected";

    /// <summary>
    /// A customer switched their sign-in to another hosting account they hold a membership of.
    /// Recorded on the refusals too: an attempt aimed at an account the person has no membership of
    /// is how one caller probes for another's account ids. The subject is the requested account id.
    /// </summary>
    public const string ActiveAccountSwitched = "ActiveAccountSwitched";

    /// <summary>A user enrolled a second factor.</summary>
    public const string TwoFactorEnabled = "TwoFactorEnabled";

    /// <summary>A user removed their second factor.</summary>
    public const string TwoFactorDisabled = "TwoFactorDisabled";

    /// <summary>A recovery code was spent in place of a TOTP code.</summary>
    public const string RecoveryCodeUsed = "RecoveryCodeUsed";

    /// <summary>A password was changed.</summary>
    public const string PasswordChanged = "PasswordChanged";

    /// <summary>The panel's first administrator was created from the installer's one-time token.</summary>
    public const string AdministratorCreated = "AdministratorCreated";

    /// <summary>
    /// A password reset was asked for. Recorded for EVERY request, including one naming an address
    /// no user holds — the journal is where a sweep through a list of guessed addresses becomes
    /// visible, and it is the only place that can see it, because the endpoint deliberately answers
    /// a known and an unknown address identically. The subject is the address as the caller typed
    /// it; the token never appears.
    /// </summary>
    public const string PasswordResetRequested = "PasswordResetRequested";

    /// <summary>
    /// A reset token was presented and refused — expired, already spent, or never issued. Its own
    /// action rather than a failed <see cref="PasswordChanged"/>, because a replayed token is the
    /// panel's only signal that a reset mail was intercepted, and it must not be lost among ordinary
    /// password changes. The entry never carries the token, only whether one was refused.
    /// </summary>
    public const string PasswordResetRefused = "PasswordResetRefused";

    /// <summary>
    /// An administrator's access was recovered, or refused recovery, from the server's own console
    /// (<c>maran admin-reset-password</c>). Recorded with the panel as actor and the claimed operator
    /// name as an unverified hint; the subject is the administrator's user id, and neither the token
    /// nor the link ever appears.
    /// </summary>
    public const string ConsoleRecovery = "ConsoleRecovery";

    /// <summary>
    /// The panel's security policy was changed: password length, forced two-factor, or the account
    /// lockout. Every one of those weakens or strengthens every account at once, so the change needs
    /// a date and an author more than most.
    /// </summary>
    public const string SecurityPolicySaved = "SecurityPolicySaved";

    /// <summary>
    /// A hosting account's owner was sent an invitation to set a first password: on account
    /// creation, and again whenever an administrator resends it. The handler that fires on account
    /// creation is idempotent and writes no second entry for a repeated <see cref="AccountAuditActions.AccountCreated"/>,
    /// but a deliberate resend for the same account is a distinct, later entry — an administrator
    /// asking again is not a duplicate delivery of the same event.
    /// </summary>
    public const string CustomerInvited = "CustomerInvited";

    /// <summary>
    /// A hosting account's owner was given a login whose first password the person creating the account
    /// chose, instead of an invitation. A distinct action from <see cref="CustomerInvited"/> on purpose:
    /// the administrator knows a credential until the owner replaces it at their first sign-in, and an
    /// auditor reading the journal must be able to tell that from a mailed invitation. The subject is the
    /// customer's login name; the entry never carries the password or its hash.
    /// </summary>
    public const string CustomerCreatedWithPassword = "CustomerCreatedWithPassword";

    /// <summary>
    /// A reseller's contact was sent an invitation to set the first password of the reseller's login,
    /// when the reseller was created. A distinct action from <see cref="CustomerInvited"/> because the
    /// login is a different kind of principal: an auditor reading the journal should be able to tell
    /// who was given a way into the panel with the power to create accounts and view as them. The
    /// subject is the reseller's login name; the entry never carries the token.
    /// </summary>
    public const string ResellerInvited = "ResellerInvited";

    /// <summary>
    /// An administrator asked the panel for a one-time invitation link to hand to a hosting
    /// account's owner themselves, instead of the panel mailing it. A distinct action from
    /// <see cref="CustomerInvited"/> on purpose: a link shown to an administrator is a credential
    /// that left the system by hand, and an auditor reading the journal should be able to tell it
    /// from a mail the panel sent. The subject is the customer's login name; the entry never
    /// carries the link, the token or its digest.
    /// </summary>
    public const string InvitationLinkIssued = "InvitationLinkIssued";

    /// <summary>
    /// An invited login's owner set their first password from the invitation link, making the login
    /// usable.
    /// </summary>
    public const string InvitationAccepted = "InvitationAccepted";

    /// <summary>
    /// An attempt to accept an invitation was refused: the token had expired, had already been
    /// spent, or never existed. Recorded the same way for all three, so the entry cannot be read to
    /// tell them apart (rules/security.md item 6, the same reasoning as
    /// <see cref="PasswordResetRefused"/>).
    /// </summary>
    public const string InvitationAcceptRefused = "InvitationAcceptRefused";

    /// <summary>
    /// A user pinned a page of the navigation to their app bar. The subject is the page's navigation
    /// key, which is a label for a screen and not a secret.
    /// </summary>
    public const string PagePinned = "PagePinned";

    /// <summary>A user removed a page of the navigation from their app bar; the subject is its key.</summary>
    public const string PageUnpinned = "PageUnpinned";

    /// <summary>
    /// A person saved their dashboard layout: which widgets and shortcuts the dashboard draws, and in
    /// what order. The subject counts the entries of each list; it never names a widget.
    /// </summary>
    public const string DashboardLayoutSaved = "DashboardLayoutSaved";

    /// <summary>
    /// An account's owner invited a person to act for the account with a limited set of features; the
    /// subject is the new login's name. Never the invitation token or its link.
    /// </summary>
    public const string SubUserInvited = "SubUserInvited";

    /// <summary>An account's owner changed which features a sub-user may use; the subject is the sub-user's login name.</summary>
    public const string SubUserGrantsChanged = "SubUserGrantsChanged";

    /// <summary>An account's owner removed a sub-user, ending their access and their sessions; the subject is the login name.</summary>
    public const string SubUserRemoved = "SubUserRemoved";

    /// <summary>
    /// An account's owner had the invitation of a still-pending sub-user sent again; the subject is the
    /// sub-user's login name. Never the invitation token or its link.
    /// </summary>
    public const string SubUserInvitationResent = "SubUserInvitationResent";

    /// <summary>
    /// An account's owner offered an existing login a place on the account's team; the subject is what
    /// the owner typed (a login name or an address), which may name nobody. Recorded the same way
    /// whether or not the offer was stored, and the entry's outcome says which: the answer to the owner
    /// is uniform on purpose and this record is where an administrator can see the difference.
    /// </summary>
    public const string SubUserMembershipOffered = "SubUserMembershipOffered";

    /// <summary>A person accepted an offer to join an account's team; the subject is the account's name.</summary>
    public const string SubUserMembershipOfferAccepted = "SubUserMembershipOfferAccepted";

    /// <summary>A person declined an offer to join an account's team; the subject is the account's name.</summary>
    public const string SubUserMembershipOfferDeclined = "SubUserMembershipOfferDeclined";

    /// <summary>
    /// An operator started viewing the panel as an account's login; the actor is the operator and the
    /// subject names the account and the login. Recorded on refusals too (issue #317).
    /// </summary>
    public const string ViewAsStarted = "ViewAsStarted";

    /// <summary>An operator pressed Back and ended a view-as session; the actor is the operator.</summary>
    public const string ViewAsEnded = "ViewAsEnded";

    /// <summary>
    /// An administrator created an API key. The subject names the key by its public prefix, its name
    /// and the scopes it was granted; never the key itself and never its digest.
    /// </summary>
    public const string ApiKeyCreated = "ApiKeyCreated";

    /// <summary>
    /// An administrator revoked an API key. The subject names the key by its public prefix, its name
    /// and the scopes it held; never the key itself and never its digest.
    /// </summary>
    public const string ApiKeyRevoked = "ApiKeyRevoked";

    /// <summary>
    /// A call authenticated with an API key reached an action. The actor is the key (never a user id),
    /// the subject is the HTTP method, the route template and the status code answered; never the
    /// concrete URL, the query string, a header, the body or the key itself.
    /// </summary>
    public const string ApiKeyCall = "ApiKeyCall";

    /// <summary>
    /// An administrator exported the audit journal. The subject records the format, the filters and the
    /// intended row count; never any row's content. Written before the file is streamed, so the export is
    /// recorded even when the client disconnects.
    /// </summary>
    public const string AuditExported = "AuditExported";

    /// <summary>
    /// An administrator changed how long the audit journal is kept. The subject records the old and the
    /// new value, with a fixed word for "keep indefinitely".
    /// </summary>
    public const string AuditRetentionChanged = "AuditRetentionChanged";

    /// <summary>
    /// The daily retention run finished, whether or not it deleted anything. The subject records the
    /// window, the cutoff, how many rows were purged and through which chain index; the actor is the
    /// panel.
    /// </summary>
    public const string AuditRetentionRun = "AuditRetentionRun";

    /// <summary>
    /// The journal's hash chain was verified, by an administrator or by the daily run. Recorded as failed
    /// when a break was found; the subject names the first broken chain index and the kind of break.
    /// </summary>
    public const string AuditChainVerified = "AuditChainVerified";

    /// <summary>
    /// An administrator exported the personal data held about an account or a user. The subject records
    /// the kind of subject and its id; never the exported content.
    /// </summary>
    public const string PersonalDataExported = "PersonalDataExported";

    /// <summary>
    /// Personal data in the journal was pseudonymised because a person or an account was deleted. The
    /// subject holds the pseudonym text and the number of rows; never the original id, name or address.
    /// </summary>
    public const string PersonalDataErased = "PersonalDataErased";
}
