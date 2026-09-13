namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a suspended hosting account is marked active
/// again, so that every module that paused a resource for <see cref="AccountSuspending"/> can put
/// back exactly what the customer had chosen.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> The same reason <see cref="AccountSuspending"/> is one:
/// Accounts may not reference the modules that own the resources, and a marketplace module must be
/// able to resume its own without the open code knowing it exists.
/// </para>
/// <para>
/// <b>Present tense, and it is load-bearing.</b> Invoked inline, before the row is written; a
/// handler that throws ABORTS the resumption and leaves the account suspended. That is the
/// recoverable direction here too — a suspended account can be resumed again, whereas an account
/// marked active whose sites are still showing the suspension stub is the panel telling a paying
/// customer their service is back when it is not.
/// </para>
/// <para>
/// <b>Restoring is not the mirror of pausing, and this is the law of this event.</b> A subscriber
/// resuming puts back only what the CUSTOMER had chosen, never everything it can reach. The worked
/// case is sites: only the panel holds <c>Site.Status</c>, so a site the customer had disabled
/// themselves must stay on the stub, and an agent-side "resume everything" would silently undo their
/// own decision. Stated generally: suspension must not overwrite state that also expresses a
/// customer choice, so resumption may only restore state suspension is known to have changed.
/// </para>
/// <para>
/// <b>Where the reversal needs no selectivity.</b> Cron and the file-transfer logins of both
/// daemons, all restored whole.
/// Suspension overwrote no customer state in either: cron's marker is orthogonal to the per-entry
/// <c>enabled</c> flag, so a job the customer had switched off comes back switched off; and locking
/// a login prefixed its stored hash rather than replacing it, so unlocking gives back the very
/// password the customer already has.
/// </para>
/// <para>
/// <b>What it is NOT.</b> Symmetrically with <see cref="AccountSuspending"/>: the account's
/// databases, the panel's own web login and any foreign crontab line were never stopped, so nothing
/// here restores them. A subscriber must not treat this event as a promise that the account was
/// fully stopped in the first place.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account about to be reactivated. Every subscriber acts on its own rows carrying this value.
/// </param>
/// <param name="Username">
/// The account's Linux system user name, carried for the reason <see cref="AccountSuspending"/>
/// carries it: it is what the agent identifies the account by, and a subscriber may not read the
/// Accounts schema to find it.
/// </param>
public sealed record AccountResuming(Guid AccountId, string Username);
