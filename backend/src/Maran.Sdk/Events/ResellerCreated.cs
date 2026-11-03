namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately after a reseller has been created, so that the
/// Identity module can issue the panel login the reseller signs in with.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> Accounts may not reference Identity
/// (rules/architecture.md "Backend: modular monolith"), so the message is declared here, in the
/// surface both already depend on. It is the reseller's counterpart of <see cref="AccountCreated"/>
/// and is delivered the same way: invoked inline, so a subscriber's failure is reported to the
/// caller instead of disappearing into a queue.
/// </para>
/// <para>
/// <b>Past tense, as its sibling is.</b> By the time it is announced the reseller row is committed,
/// so a failing subscriber leaves a reseller with no login; the recovery is a repeat of the same
/// issuance, which the subscriber must tolerate, not a rollback.
/// </para>
/// </remarks>
/// <param name="ResellerId">The reseller that was created; the login issued for it carries this id.</param>
/// <param name="Username">The reseller's name, which is also the login name.</param>
/// <param name="OwnerEmail">The address the invitation is sent to, as supplied by whoever created the reseller.</param>
/// <param name="OwnerLanguage">
/// The contact's language, one of <c>PanelLanguages.Supported</c>: the invitation is written in it,
/// not in the language of the administrator whose request created the reseller.
/// </param>
public sealed record ResellerCreated(Guid ResellerId, string Username, string OwnerEmail, string OwnerLanguage);
