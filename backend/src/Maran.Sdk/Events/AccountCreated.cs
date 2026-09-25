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
public sealed record AccountCreated(Guid AccountId, string Username, string OwnerEmail);
