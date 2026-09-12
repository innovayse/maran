//! The per-account lock every mutating cron operation runs under.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, PoisonError};

use maran_agent_core::validation::system::name::AccountName;
use tokio::sync::{Mutex as AsyncMutex, OwnedMutexGuard};

/// The registry of per-account crontab locks.
///
/// A map and not one process-wide lock, because the unit of serialisation here
/// is the ACCOUNT. Every mutating operation in this area takes exactly one
/// [`AccountName`] and rewrites exactly one spool file, `crontab -u <account>`;
/// two customers' crontabs are two files that share no line, no marker and no
/// entry id, so serialising them against each other would make one customer's
/// slow spool everyone else's queue for no gain at all. The firewall's lock is
/// process-wide for the opposite reason — one kernel ruleset, no tenant to
/// scope it to.
///
/// The outer lock is a `std::sync::Mutex` because it is held for the length of
/// a map lookup and nothing else; the inner ones are `tokio::sync::Mutex`
/// because they are what an operation holds across its read and its install.
///
/// Entries are never removed, for the reason
/// the backup area's own per-account lock gives: the map is bounded by the
/// number of accounts this host has ever had a cron operation run against, at
/// one `Arc` and one name each, and removing an entry when its last guard drops
/// would need the removal and the next lookup to be one atomic step — getting
/// that wrong hands two operations two different locks for one account, which
/// is precisely the failure this lock exists to prevent.
static ACCOUNT_LOCKS: Mutex<Option<HashMap<String, Arc<AsyncMutex<()>>>>> = Mutex::new(None);

/// Takes `account`'s crontab lock, waiting until it is free.
///
/// # Why the crontab needs a lock at all
///
/// **Every mutating operation in this area is a read-modify-write of the WHOLE
/// table.** There is no line-level edit anywhere: an operation runs
/// `read_crontab`, parses the text into a
/// [`CrontabDocument`](crate::cron::model::crontab_document::CrontabDocument),
/// changes one thing, and hands `crontab(1)` a freshly rendered table that
/// replaces everything. `crontab(1)`'s own spool locking does not help, because
/// the read and the install are two separate invocations of it with this
/// agent's decision in between.
///
/// So two operations that overlap both read the same table and the second
/// install silently discards the first one's change. Three of those losses have
/// been enumerated, and one of them defeats a control the product sells:
///
/// - A `create_cron_entry` overlapping `set_account_cron_suspended(true)` reads
///   the pre-suspension document, appends its entry with that document's
///   `suspended` flag, and renders — and the render normalises **every managed
///   line** to the document's flag. Whichever install lands last decides the
///   account's whole suspension state. When the create lands last the
///   suspension answered `Ok`, the panel recorded the account as suspended, and
///   every one of the customer's jobs keeps firing. **Nothing will ever notice**
///   — the panel keeps no cron rows at all, so the crontab is the only record
///   of that state there is and there is no second copy to reconcile against.
/// - A `delete_cron_entry` overlapping a `create_cron_entry` loses the deletion:
///   the create installs a table that still holds the deleted entry, whose files
///   the deletion has already removed.
/// - Two concurrent `create_cron_entry` calls both pass the duplicate check
///   against the same document and both write their command file; one table
///   wins, and the loser's file stays in the customer's home forever, since the
///   file is only taken away again when the install was REFUSED.
///
/// # Why the whole read-modify-write, and not just the install
///
/// The lock is taken as the first statement of each operation, before
/// `read_crontab`, and released when the operation returns. A lock that covered
/// only the install would serialise the two writes and change nothing at all:
/// the losing writer's table was already rendered from a document it read
/// outside the lock, so it would still overwrite the winner's. The critical
/// section is the read, the parse, the decision, the render and the install
/// together, or it is decoration.
///
/// The guard is owned, so a caller keeps it in a local and the lock is released
/// on every path out — including every error path and a panic. There is no
/// unlock to forget.
///
/// # Waiting, and not refusing
///
/// The backup area's lock refuses without waiting and this one waits, and the
/// difference is the length of what is being guarded. A
/// backup holds its lock for as long as a twenty-gigabyte account takes to
/// archive, so a caller that waited would hold an RPC open for hours. A cron
/// mutation holds this one for two `crontab(1)` invocations and some string
/// work — milliseconds — so the wait is shorter than the round trip that
/// delivered the request.
///
/// What the panel does with each is the other half of the argument. A refusal
/// would have to be a new error variant the panel maps to something a person
/// reads, and `SetAccountCronSuspended` is driven by an unattended Wolverine
/// handler with no operator watching: a refused suspension there would be a
/// suspension that did not happen and a message nobody reads, which is the same
/// outcome as the race this lock exists to close, reached by a politer route. A
/// waited one is a slower success, and the panel sees an ordinary `Ok`.
///
/// # The scope and the lifetime of this lock, stated where a reader needs it
///
/// **Process-local, and only for as long as this agent process lives.** It
/// serialises the RPCs of one running `maran-agent` against each other and
/// against nothing else. It is not an on-disk or kernel lock, so it does NOT
/// serialise against: a second agent binary an operator runs by hand, an
/// installer step, `crontab -e` run by the account itself, or an administrator
/// editing the spool. That is the same scope the other two locks in this
/// workspace have, and it is sound for the shape the agent is deployed in — one
/// root daemon per host, systemd-managed, its socket bound exclusively — but it
/// is a real boundary rather than a total one. Closing the rest needs a
/// compare-and-swap: the document carrying the bytes it was parsed from and the
/// install refusing when the spool no longer matches them. That is a change to
/// the [`CronHost`](crate::cron::cron_host::CronHost) contract and is not in
/// this lock.
///
/// A poisoned registry lock is recovered from rather than propagated: it can
/// only be poisoned by a panic while a map lookup was in progress, a `HashMap`
/// insert either happened or did not, and a root daemon that refused every cron
/// operation for the rest of its life over one unrelated panic would be worse
/// than what is being guarded against.
///
/// # The requirement this places on every caller
///
/// **An operation that takes this lock MUST be invoked from
/// `tokio::task::spawn_blocking`, and MUST NOT be awaited on a runtime worker.**
/// The guard is taken with `blocking_lock`, which is what [`tokio::sync::Mutex`]
/// provides for synchronous code sharing a lock with asynchronous code. Every
/// method of `CronHost` already carries that obligation for its own reason —
/// two of them fork and block in `waitpid` — and `services/cron/cron_service.rs`
/// meets it today by handing every operation to `services/wire/run_blocking.rs`.
///
/// # Panics
///
/// `blocking_lock` panics when it is called from inside an asynchronous
/// context. **That panic is the enforcement of the requirement above, not a
/// hazard to work around**: it is a programming error rather than an input, and
/// it fails on the first call, loudly, instead of silently stalling every other
/// in-flight command on the same worker. It is tokio's own check, it is not
/// gated by `debug_assertions`, and it does not fire on the blocking pool where
/// blocking is the point.
pub(crate) fn cron_lock(account: &AccountName) -> OwnedMutexGuard<()> {
    let mut registry = ACCOUNT_LOCKS.lock().unwrap_or_else(PoisonError::into_inner);

    let locks = registry.get_or_insert_with(HashMap::new);
    let lock = Arc::clone(
        locks
            .entry(account.as_str().to_owned())
            .or_insert_with(|| Arc::new(AsyncMutex::new(()))),
    );

    // Dropped before the wait below, so the registry is never held across
    // anything but its own lookup — waiting for one account's crontab must not
    // stop another account's operation finding its own lock.
    drop(registry);

    lock.blocking_lock_owned()
}

#[cfg(test)]
#[path = "../tests/cron/cron_lock_tests.rs"]
mod tests;
