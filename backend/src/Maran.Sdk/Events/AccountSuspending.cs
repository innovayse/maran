namespace Maran.Sdk.Events;

/// <summary>
/// Announced by the Accounts module immediately BEFORE a hosting account is marked suspended, so
/// that every module driving a resource on the account's behalf can stop it first.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why an Sdk event and not a call.</b> The resources that must stop belong to other modules —
/// the Sites module owns the vhosts — and the Accounts module may not reference any of them
/// (rules/architecture.md "Backend: modular monolith", enforced by <c>ModuleIsolationTests</c>). The
/// message is therefore declared here, in the contract surface every module already depends on, and
/// each module subscribes to it. That is also what lets a paid marketplace module pause its own
/// resources without the open code knowing it exists.
/// </para>
/// <para>
/// <b>Present tense, and it is load-bearing.</b> This is <c>AccountSuspending</c>, not
/// <c>AccountSuspended</c>: it is invoked inline, before the row is written, and a handler that
/// throws ABORTS THE SUSPENSION and leaves the account exactly as it was. That is the recoverable
/// direction. An account still active can be suspended again once whatever refused is fixed,
/// whereas an account marked <c>Suspended</c> whose sites are still serving is a lie the panel has
/// already told billing — and billing is the one caller of this operation.
/// </para>
/// <para>
/// <b>What it covers, and what it still does not.</b> Four things: the account's own Linux login,
/// every vhost of the account, every managed entry of its crontab, and every
/// <c>&lt;account&gt;_*</c> SFTP login. Cron carries a marker ORTHOGONAL to the per-entry
/// <c>enabled</c> flag, because that flag is the customer's own choice and reusing it would make a
/// resume switch back on the jobs they had turned off themselves. The SFTP logins are locked one by
/// one because each is its own passwd entry sharing the account's uid, so the <c>usermod --lock</c>
/// on the account reaches none of them — a suspended customer kept a working WRITE credential into
/// their home until this cascade grew that half.
/// </para>
/// <para>
/// It does <b>not</b> stop the account's DATABASES, does <b>not</b> block the panel's own web login,
/// and does <b>not</b> touch crontab lines the panel did not write — a crontab is not the panel's
/// file, so a hand-added job keeps firing and is counted rather than deleted. A subscriber must not
/// assume otherwise.
/// </para>
/// <para>
/// <b>It is not the thing that PROVES the suspension either.</b> A subscriber that does not exist
/// cannot throw, so an unhandled event and a module with nothing to pause look identical from the
/// publisher's side. What closes that gap is the host attestation the Accounts handler performs
/// afterwards, which asks the machine what it is serving rather than asking the panel what it
/// remembers asking for.
/// </para>
/// </remarks>
/// <param name="AccountId">
/// The account about to be suspended. Every subscriber acts on its own rows carrying this value and
/// touches nothing else — this is a cascade, not an invitation to reach across a schema.
/// </param>
/// <param name="Username">
/// The account's Linux system user name, which is what the agent identifies the account by and what
/// an operator recognises it as. Carried rather than looked up, because a subscriber may not read
/// the Accounts schema.
/// </param>
public sealed record AccountSuspending(Guid AccountId, string Username);
