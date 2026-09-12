//! SetAccountLoginsLocked: every key into one account's home turned at once,
//! and — on the locking direction — every session already inside it ended.

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;

use crate::accounts::take_account_lock;
use crate::logins::account_logins::account_logins;
use crate::logins::end_account_sessions::end_account_sessions;
use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;
use crate::logins::model::account_lock_outcome::AccountLockOutcome;

/// The flag that prefixes a password hash so nothing can match it.
const LOCK: &str = "--lock";

/// The flag that removes that prefix again.
const UNLOCK: &str = "--unlock";

/// Locks, or unlocks, every file-transfer login `account` holds, and answers
/// with what the host then shows and how many sessions were ended.
///
/// # What the answer carries, and why the cull count is in it
///
/// [`AccountLockOutcome`]: the account's logins as the host holds them after the
/// operation, and [`AccountLockOutcome::sessions_ended`] — the figure the cull
/// already produced and which, until this returned it, reached nothing but this
/// agent's own log. An operator's record of a suspension has to be able to state
/// that privileged action, and a record cannot state what the caller was never
/// told. The count is [`None`] on the unlock direction, `Some(0)` when the cull
/// ran and matched nothing, and never a number at all when the cull failed: the
/// three are separate values rather than one number whose meaning has to be
/// guessed at.
///
/// # Why this operation has to exist at all
///
/// Because `usermod --lock <account>` does not reach these logins. Each is its
/// OWN passwd entry, named `<account>_<name>` and created with
/// `useradd --non-unique --uid <account uid>` so that it writes as the account
/// — which means locking the account's own entry leaves every one of them
/// authenticating. That is not "a site keeps serving": it is a live write
/// credential into a customer's home surviving the suspension that was meant
/// to take it away.
///
/// # Why it is here and not in one protocol's area
///
/// An account can hold logins of both protocols, and a suspension that asked
/// only the area it happened to be written in would leave the other's
/// credential open. A facility two areas need is its own area
/// (rules/architecture.md), and this is the operation where being a lodger in
/// the first area that wanted it is a security defect rather than an
/// inconvenience.
///
/// # Where the list comes from
///
/// The host's password database, through
/// [`account_logins`](super::account_logins::account_logins), and never a list
/// the panel supplies. A list can only describe what the panel remembers
/// creating; a login it has forgotten is exactly the one that would keep
/// working.
///
/// # Locking a login is not the whole of suspending it
///
/// The marker this operation writes is consulted at AUTHENTICATION, so it
/// refuses the account's NEXT login and does nothing to a session already open:
/// neither sshd nor vsftpd re-authorises a session it has admitted. So when it
/// is asked to LOCK, this operation also ends every process running as the
/// account's uid — `end_account_sessions` (not linked: it is private to this
/// area, and this entry point is public), whose own doc comment carries the
/// confinement argument and which is a privileged surface with an outstanding
/// second review (`docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`).
///
/// **Order: lock first, then cull.** The other way round leaves a window in
/// which the customer's client simply reconnects, which would make the cull
/// theatre. The cull runs only on the locking direction — a resume has no
/// sessions to end, and ending an account's processes as it comes BACK would
/// kill whatever it had legitimately started in the meantime.
///
/// **What it costs the customer**, because a caller should know before it calls:
/// a transfer in flight is cut at whatever byte it had reached, so the account's
/// home can be left holding a partial file. Nothing deletes it and nothing marks
/// it. The sentence an operator should read is in `docker/README.md`.
///
/// A cull that fails takes the whole operation down with it
/// ([`LoginsError::SessionCullFailed`]), rather than reporting a suspension
/// nobody observed. That is safe to retry precisely because of the order above:
/// the logins are already locked, so the failure leaves an account refusing new
/// logins and possibly holding an open session — which is exactly what this
/// product shipped before the cull existed — and never one whose access was
/// restored.
///
/// # What it does not do
///
/// Delete anything, and change no password. Locking prefixes the stored hash
/// and unlocking removes that prefix, so the resume gives the customer back the
/// credential they already had rather than a new one they would have to be
/// told about. The jails, the bind mounts and the files behind the logins are
/// untouched. Nor does it touch the entries counted in
/// [`super::AccountLoginSet::unmanaged`]: this agent did not create those, and it says
/// how many there were instead of turning off a credential somebody else
/// arranged.
///
/// # Why the host is read twice
///
/// Once to learn what to act on, and once afterwards to see what the host now
/// says. The second read is an observation and not a prediction: `usermod`
/// answers zero on a login it did nothing to, so a set built from "what I asked
/// for" would report a suspension nobody can see. The returned value is the one
/// the panel attests on.
///
/// # Idempotency, and the one login that does not come back
///
/// `usermod --lock` and `usermod --unlock` are both idempotent and both answer
/// zero, so either direction may be repeated. The exception is measurable and
/// is stated rather than hidden: a login that has NO password at all cannot be
/// unlocked — `usermod` says so and still exits zero — so it stays locked after
/// a resume. Every login this agent creates is given a password in the same
/// operation that creates it, so such a login was made by hand; the answer
/// reports it as still locked and the panel says so instead of pretending
/// otherwise.
///
/// The cull is idempotent in the same way and for a simpler reason: signalling
/// the processes of a uid that has none is `pkill` answering "nothing matched",
/// which this operation reads as the state it wanted. So a suspension may be
/// re-issued freely, and re-issuing it is the documented remedy for a cull that
/// failed.
///
/// An account with no logins is a success that locks nothing — and, on the
/// locking direction, it still ends the account's sessions. Those are not the
/// same question: the logins are what the panel created, and the sessions are
/// whatever is running as the account's uid, including one opened through a
/// credential this agent does not manage (the entries
/// [`super::AccountLoginSet::unmanaged`] counts). It is the one place those entries are
/// covered rather than merely counted.
///
/// # The lock
///
/// The hosting account's lock (`crate::accounts::account_lock`) is taken at this
/// entry and held until it returns. The operation enumerates the account's
/// logins, acts on each of them and then ends the account's sessions, which is
/// one read-modify-write and not several: without the lock, a login created
/// between the enumeration and the last `usermod` is a login this suspension
/// never sees, and a password change landing in the same window replaces the very
/// field this operation is writing.
///
/// The cull is inside that critical section for a reason of its own, and it is
/// the reason the lock matters more here than it did before: a `fork_as_account`
/// child of some other operation of this agent runs AS THE ACCOUNT'S UID, so a
/// cull racing one would kill our own privileged-work child mid-write. Holding
/// the account's lock across the cull is what says no such child exists.
///
/// It is the SAME lock `create_sftp_user`, `set_sftp_password`, the account's
/// deletion and the four backup operations take — **no new lock is introduced**
/// (rules/rust.md, "What this agent serialises"), and this operation takes that
/// one and nothing else, so it adds no edge at all to the wait-for graph: the
/// lock never waits, it refuses. It is process-local, and serialises this binary
/// against itself and nothing else — not an operator's own `usermod`, not a
/// second agent binary.
///
/// The honest cost of a lock that refuses rather than queues: **a suspension
/// issued while a backup of the same account is running is refused for as long
/// as that backup takes.** A suspension is the caller least able to absorb a
/// silent refusal, so the panel must retry [`LoginsError::AccountBusy`] — that
/// is a panel-side decision and it is not made here.
///
/// # Errors
///
/// - [`LoginsError::AccountBusy`] when another operation for the account is
///   already running. Nothing was locked or unlocked.
/// - [`LoginsError::AccountMissing`] when the password database cannot be read
///   or holds no row for the account.
/// - [`LoginsError::SpawnFailed`] when `usermod` refuses a login for any reason,
///   or could not be run at all. The first refusal stops the operation: a
///   partially locked account is a state the caller must be able to see, and
///   the attestation will report exactly which logins are still open.
/// - [`LoginsError::StatusUnreadable`] when `passwd -S` printed something this
///   agent cannot read for one of the logins.
/// - [`LoginsError::SessionCullRefusedUid`] and
///   [`LoginsError::SessionCullRefusedHome`] when the account's own passwd row is
///   not one this agent will cull the sessions of. The logins ARE locked; the
///   sessions were not touched.
/// - [`LoginsError::SessionCullFailed`] when `pkill` refused. The logins are
///   locked and some of the account's processes may have been signalled, so the
///   suspension is reported as failed and is re-issued rather than believed.
pub fn set_account_logins_locked(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    locked: bool,
) -> Result<AccountLockOutcome, LoginsError> {
    // Owned guard, so the lock is released by every path out of the call below,
    // including every error path and a panic.
    let _guard = take_account_lock(account).ok_or(LoginsError::AccountBusy)?;

    set_account_logins_locked_under_lock(host, distro, account, locked)
}

/// The locking itself, with the account's lock ALREADY held.
///
/// Split from [`set_account_logins_locked`] for the reason `create_sftp_user`'s
/// body is split from its entry point: the lock is a process-wide static, and a
/// suite driving the public entry for one account name on the harness's own
/// threads would refuse itself — a flaky suite whose flake says nothing about
/// the code. The exclusion is tested for what it is, once; everything else about
/// a suspension is exercised through here.
///
/// # Errors
///
/// Every variant [`set_account_logins_locked`] documents except
/// [`LoginsError::AccountBusy`], which is the entry point's own answer — and that
/// exclusion is why this function, not the entry point, is where the suite reads
/// [`AccountLockOutcome::sessions_ended`]: a busy account never reaches the cull,
/// so `AccountBusy` is the one outcome whose absence of a count is the lock's
/// answer rather than the cull's.
pub(crate) fn set_account_logins_locked_under_lock(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    account: &AccountName,
    locked: bool,
) -> Result<AccountLockOutcome, LoginsError> {
    let flag = if locked { LOCK } else { UNLOCK };

    for login in account_logins(host, distro, account)?.logins {
        let outcome = host.run(distro.usermod_binary(), &[flag, &login.name])?;
        if outcome.status != 0 {
            return Err(LoginsError::SpawnFailed {
                code: outcome.status,
            });
        }
    }

    let sessions_ended = if locked {
        let ended = end_account_sessions(host, distro, account)?;
        tracing::info!(
            account = account.as_str(),
            processes_signalled = ended,
            "ended the suspended account's open sessions"
        );

        Some(ended)
    } else {
        None
    };

    Ok(AccountLockOutcome {
        logins: account_logins(host, distro, account)?,
        sessions_ended,
    })
}

#[cfg(test)]
#[path = "../tests/logins/set_account_logins_locked_tests.rs"]
mod tests;
