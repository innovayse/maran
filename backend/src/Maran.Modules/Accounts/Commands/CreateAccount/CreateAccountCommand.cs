using System.Text.Json.Serialization;
using Maran.Sdk.Contracts;
using Maran.SharedKernel.Constants;
using Maran.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Maran.Modules.Accounts.Commands.CreateAccount;

/// <summary>
/// Creates a hosting account: its Linux user, home directory and disk quota through the agent,
/// and then the row that records it (spec §8 — an Account IS a system user, and the isolation
/// between customers is the operating system's).
/// </summary>
/// <remarks>
/// This command is bound directly by <c>AccountsController.CreateAsync</c>; there is no separate
/// request type. <paramref name="IpAddress"/> and <paramref name="UserAgent"/> are the two fields a
/// caller must not be able to state, and the pair of attributes on them is what makes that true —
/// see their own documentation.
/// </remarks>
/// <param name="Name">The account's unique short name, in the agent's account alphabet and not a reserved system name.</param>
/// <param name="PrimaryDomain">The account's primary domain.</param>
/// <param name="PlanId">The id of the plan bounding this account's resource limits.</param>
/// <param name="OwnerEmail">
/// The account's own contact address, stored on <c>Account.OwnerEmail</c> and carried to Identity
/// on <see cref="Maran.Sdk.Events.AccountCreated"/>, which stores it again on the login it creates.
/// The two copies start equal and are allowed to diverge afterwards — see <c>Account.OwnerEmail</c>'s
/// own remarks for why a second copy is the deliberate choice: it is what lets an administrator
/// resend an invitation for an account whose login was never successfully created.
/// </param>
/// <param name="OwnerLanguage">
/// The owner's language, one of <see cref="PanelLanguages.Supported"/>; the owner's invitation is
/// written in it (issue #194). Optional on the wire and English when left out, so a caller that
/// predates the field keeps the behaviour it had, minus the creator's own language leaking into the
/// owner's mail.
/// </param>
/// <param name="ResellerId">
/// The reseller that is to own the account. An administrator may name one, or none for an account of
/// its own; a reseller always creates for itself, whatever this says as long as it is itself or
/// absent, and naming any other reseller is answered <c>ResellerNotFound</c>, the same as for one that
/// does not exist. It is an input like the plan, validated against the caller, and not a
/// server-established member: the owner of a reseller's account is established by the reseller's own
/// token (<see cref="Services.ResellerScope.ResolveOwner"/>), so there is nothing here a body could
/// forge that the caller's identity does not overrule.
/// </param>
/// <param name="IpAddress">
/// The caller's address, recorded in the audit journal. Read from the connection by the controller
/// and never bound from the request: <c>JsonIgnore</c> keeps it out of the JSON body — a
/// <c>[FromBody]</c> parameter is handed to an input formatter, which the model-binding pipeline
/// never sees, so <c>BindNever</c> alone would leave it writable — and <c>BindNever</c> keeps it out
/// of the query and form pipelines, where <c>JsonIgnore</c> means nothing. The two cover disjoint
/// pipelines, so both are always present. The default exists so that <c>[ApiController]</c>'s
/// implicit-required validation does not reject every body for omitting a field the body is
/// forbidden to carry.
/// </param>
/// <param name="UserAgent">
/// The caller's user agent, recorded in the audit journal. Server-established and guarded exactly as
/// <paramref name="IpAddress"/> is, for the same reason.
/// </param>
/// <param name="LoginMode">
/// How the owner gets a way in: <see cref="OwnerLoginMode.Invitation"/> (the default) mails a link the
/// owner uses to choose a password, <see cref="OwnerLoginMode.InitialPassword"/> sets the password the
/// creator states and requires the owner to change it at the first sign-in (issue #194).
/// </param>
/// <param name="InitialPassword">
/// The owner's first password; stated exactly when <paramref name="LoginMode"/> is
/// <see cref="OwnerLoginMode.InitialPassword"/>. A <see cref="SensitiveString"/>, which reads from the
/// same JSON string a plain field would (the panel registers its converter), so the command's printed
/// form is <c>[redacted]</c> and not the password. Only its coupling with the mode is checked here;
/// whether it is strong enough is Identity's rule, kept in one place there.
/// </param>
/// <param name="InodeLimit">
/// The most files and directories this account may own. <c>0</c> is no limit; left out, the account
/// follows its plan, which states none. A reseller may not state a figure above its plan's.
/// </param>
/// <param name="ProcessLimit">
/// The most processes and threads this account may run at once; at least <c>1</c>. Left out, the
/// account follows its plan. A reseller may only state a figure at or below its plan's.
/// </param>
/// <param name="OpenFilesLimit">
/// The most files one login session of this account may hold open. <c>0</c> is no limit; left out, the
/// account follows its plan, which states none. It reaches login sessions only, not php-fpm workers.
/// </param>
/// <param name="ShellAccess">
/// Whether the account's system user gets an interactive shell for SSH and SFTP logins instead of
/// none. Off by default: a shell is a way to run code as the account.
/// </param>
/// <param name="BackupEnabled">
/// Whether the account is included in scheduled backups. On by default; carried to the Backups module
/// on <see cref="Maran.Sdk.Events.AccountCreated"/>, which records the opt-out.
/// </param>
/// <param name="SuppressInvitationMail">
/// Server-established, never bound from a request: true only when an import creates the account (issue
/// #127, decision D7), so the owner's invitation is made but not mailed to a server that is not live yet.
/// It is carried to Identity on <see cref="Maran.Sdk.Events.AccountCreated"/>. Guarded like the caller's
/// address, because a body that could set it would let any creator leave an owner without a mail.
/// </param>
public sealed record CreateAccountCommand(
    string Name,
    string PrimaryDomain,
    Guid PlanId,
    string OwnerEmail,
    string OwnerLanguage = PanelLanguages.English,
    Guid? ResellerId = null,
    [property: JsonIgnore][property: BindNever][BindNever] string IpAddress = "",
    [property: JsonIgnore][property: BindNever][BindNever] string UserAgent = "",
    OwnerLoginMode LoginMode = OwnerLoginMode.Invitation,
    SensitiveString? InitialPassword = null,
    long? InodeLimit = null,
    int? ProcessLimit = null,
    int? OpenFilesLimit = null,
    bool ShellAccess = false,
    bool BackupEnabled = true,
    [property: JsonIgnore][property: BindNever][BindNever] bool SuppressInvitationMail = false);
