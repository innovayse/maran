using Maran.Sdk.Contracts;
using Maran.SharedKernel.Security;

namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately after a hosting account has been created, so that
/// the Identity module can issue the panel login that owns it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> Accounts may not reference Identity
/// (rules/architecture.md "Backend: modular monolith", enforced by <c>ModuleIsolationTests</c>), so
/// the message is declared here, in the surface both already depend on.
/// </para>
/// <para>
/// <b>Past tense, and it is load-bearing.</b> Its sibling <see cref="AccountDeleting"/> is present
/// tense and promises that a throwing subscriber aborts the operation. This one cannot make that
/// promise and does not pretend to: by the time it is announced the agent has already created the
/// system user and the account row is committed, and there is no transaction spanning the two. It
/// is nevertheless INVOKED inline rather than published, so a subscriber's failure is reported to
/// the caller instead of disappearing into a queue — the account then exists without a login, the
/// command answers with the failure, and "resend invitation" is the one recovery path, shared with
/// the case of a panel that has no SMTP configured yet.
/// </para>
/// </remarks>
/// <param name="AccountId">The account that was created; the login issued for it carries this id.</param>
/// <param name="Username">The account's Linux system user name, which is also the login name.</param>
/// <param name="OwnerEmail">The address the invitation is sent to, as supplied by whoever created the account.</param>
/// <param name="OwnerLanguage">
/// The owner's language, one of <c>PanelLanguages.Supported</c>: the invitation is written in it,
/// not in the language of the administrator whose request created the account (issue #194).
/// </param>
/// <param name="LoginMode">
/// How the owner gets their first way in: an invitation mail (the default) or a password the creator
/// chose. Identity reads it; no other subscriber needs it.
/// </param>
/// <param name="InitialPassword">
/// The owner's first password when <paramref name="LoginMode"/> is
/// <see cref="OwnerLoginMode.InitialPassword"/>, otherwise null. A <see cref="SensitiveString"/> because
/// this record is a compiler-generated <c>ToString()</c> away from printing every member: the type masks
/// the value in logs, exception text and the debugger. The event is only ever invoked in-process and is
/// never published to a durable queue, so the value is never written to a table.
/// </param>
/// <param name="BackupEnabled">
/// False when the creator asked for the account to be left out of scheduled backups. Backups reads it;
/// the default keeps an account in the schedule.
/// </param>
/// <param name="SuppressInvitationMail">
/// True when the account is created by an import (issue #127): the owner's login and invitation token are
/// made as usual, but the mail is not sent, because the owner must not be invited to a server that is not
/// live yet. The administrator issues the link at cut-over (<c>IssueInvitationLink</c>). Identity reads it;
/// no other subscriber needs it.
/// </param>
public sealed record AccountCreated(
    Guid AccountId,
    string Username,
    string OwnerEmail,
    string OwnerLanguage,
    OwnerLoginMode LoginMode = OwnerLoginMode.Invitation,
    SensitiveString? InitialPassword = null,
    bool BackupEnabled = true,
    bool SuppressInvitationMail = false);
