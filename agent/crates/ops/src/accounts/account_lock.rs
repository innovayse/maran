//! The per-account lock, and the operations that serialise on it.

use std::collections::HashMap;
use std::sync::{Arc, Mutex, PoisonError};

use maran_agent_core::validation::system::name::AccountName;
use tokio::sync::{Mutex as AsyncMutex, OwnedMutexGuard};

/// The registry of per-account locks.
///
/// A map and not one process-wide lock, because the unit of serialisation here
/// is the ACCOUNT, not the host: two customers' accounts share nothing the
/// operations below touch — not a home, not a passwd entry, not a pool file,
/// not an artifact name — and serialising them against each other would make
/// one customer's slow database everyone's nightly window. The firewall's own
/// lock is process-wide for the opposite reason: there is one kernel ruleset
/// and no tenant to scope it to.
///
/// **It lives in `accounts` and not in `backup`, where it started, because it
/// is the ACCOUNT's lock.** It began as the thing that kept two backups of one
/// account apart, which made `ops::backup` look like its owner; what the
/// concurrency audit established is that the state it really protects is the
/// account's existence on the host — the passwd entry, the home, the jail and
/// the pool files — and that the backup area was one of four callers rather
/// than the subject. It is not in `agent-core` either, and that is a deliberate
/// answer to the audit's suggestion: `agent-core` holds validated types and the
/// privilege wrappers, has no `tokio` dependency at all, and every caller of
/// this lock is in `ops`. Moving it there would put a runtime concept in the
/// crate whose whole job is "a constructed value is a valid value".
///
/// The outer lock is a `std::sync::Mutex` because it is held for the length of
/// a map lookup and nothing else; the inner ones are `tokio::sync::Mutex`
/// because they are the thing an operation holds for as long as a backup takes.
///
/// Entries are never removed. The map is therefore bounded by the number of
/// accounts this host has ever run one of these operations for, at one `Arc`
/// and one name each — a few kilobytes on a full server. Removing an entry when
/// its last guard drops would need the removal and the next lookup to be one
/// atomic step, and
/// getting that wrong hands two operations two different locks for one account,
/// which is precisely the failure the lock exists to prevent.
static ACCOUNT_LOCKS: Mutex<Option<HashMap<String, Arc<AsyncMutex<()>>>>> = Mutex::new(None);

/// Takes `account`'s lock without waiting, or answers `None` because another
/// operation holds it.
///
/// # Who takes it, and what it therefore excludes
///
/// `AccountOperations::delete`; the two transfer daemons' login operations
/// (`sftp::create_sftp_user`, `sftp::set_sftp_password`, `sftp::delete_sftp_user`,
/// `ftps::create_ftps_user`, `ftps::set_ftps_password`, `ftps::delete_ftps_user`);
/// `logins::set_account_logins_locked`; `php::write_pool`; and the four backup
/// operations (`create_backup`, `restore_backup`, `delete_backup`,
/// `recover_restores`). Those are the operations that make or unmake the
/// account's identity on this host, or write a file that names it, and all three
/// of the audit's measured interleavings are pairs drawn from that list — a
/// deletion against a jail creation, a deletion against a restore's home swap,
/// and a deletion against a php-fpm pool write.
///
/// **This list is the inventory, and it has been wrong before.** It once said a
/// password change and a suspension were deliberately not takers, at a time when
/// both had become ones; a reader who trusted it would have concluded that
/// `set_sftp_password` held nothing. Anything added to the list above is added
/// here in the same change, and the two-lock enumeration below with it.
///
/// The pool writer is on the list rather than its callers. `sites::create_site`
/// and `sites::update_site_php_version` reach it through
/// `sites::write_site_pool`, and taking the lock at the one place the pool file
/// is actually written means no future caller of that writer can forget to.
/// What the deletion does in ADDITION — sweeping the pools last, immediately
/// before `userdel` — is kept: it narrowed this window before the lock existed,
/// and it is still the ordering that makes the sweep's own `php-fpm -t` run
/// while the account is still there.
///
/// **A password change and a suspension ARE on the list, and used to be named
/// here as the examples of what was not.** They earned their place by becoming
/// read-modify-writes of a shadow field rather than single writes:
/// `set_sftp_password` and `set_ftps_password` read the stored password,
/// `chpasswd`, and then re-assert the suspension they observed, so a suspension
/// landing between the read and the write would otherwise be undone on the
/// strength of a fact that had expired; `set_account_logins_locked` enumerates
/// the account's logins and locks each, which is the same shape. The cost the
/// old sentence warned about is real and is accepted: a password change IS now
/// refused while a nightly backup of that account runs, with `AccountBusy`, and
/// the panel retries.
///
/// An operation that reads nothing it then writes back — a quota — is still not
/// a taker.
///
/// The lock is taken at an operation's ENTRY and never by anything it calls, so
/// there is no nesting to refuse itself on: `delete` calls
/// `remove_account_sftp`, `remove_account_ftps` and `remove_account_pools`, and
/// `create_sftp_user` calls `set_sftp_password`, none of which take it. That is
/// what the `*_under_lock` split in `set_sftp_password`, `set_ftps_password`,
/// `delete_sftp_user`, `delete_ftps_user` and `set_account_logins_locked` is
/// for: the entry point takes the lock and does the checks that need it, and the
/// inner function is what a caller already holding the lock reaches. `php::remove_pool` is
/// deliberately NOT a taker for exactly that reason — the deletion's own sweep
/// calls it while holding this lock — and it does not need to be: a removal
/// cannot leave behind a pool naming a user who has gone, which is the state
/// being excluded.
///
/// # Without waiting, and that is what makes deadlock impossible
///
/// A second operation for an account is refused rather than queued. Six typed
/// variants carry that refusal, one per area that takes this lock, and every
/// taker named above produces exactly one of them:
///
/// - `AccountError::Busy` — the deletion,
/// - `SftpError::AccountBusy` — the three SFTP login operations,
/// - `FtpsError::AccountBusy` — the three FTPS ones,
/// - `LoginsError::AccountBusy` — the suspension of an account's logins,
/// - `PhpOpError::AccountBusy` — the pool write,
/// - `BackupError::AlreadyRunning` — the four backup operations.
///
/// **This enumeration is the inventory too, and it was wrong for as long as
/// the two newest areas existed.** It listed four of these six, omitting the
/// logins and FTPS variants, which is the defect `rules/architecture.md` rates
/// at the severity of the behaviour: a reader counting the ways this refusal can
/// reach a caller would have concluded that locking an account's logins, and
/// every FTPS login operation, could not report it. Derived rather than
/// remembered, and the command is the check:
/// `grep -rn -A3 take_account_lock agent/crates/ops/src --include=*.rs |
/// grep -v /tests/ | grep -oE '(Sftp|Ftps|Logins|Backup|Account|PhpOp)Error::[A-Za-z]+' |
/// sort -u` prints the list above and nothing else. A seventh area is added here
/// in the same change that adds it to the takers.
///
/// # What the caller sees
///
/// All six become one code on the wire, `ERROR_CODE_ACCOUNT_BUSY` (`common.proto`),
/// mapped by each area's status module. The code says one thing and the areas do
/// not widen it between them: **this account's lock was held, so nothing
/// happened** — the refusal is the operation's first statement, before any tool
/// runs and before any file is touched. It is therefore safe for the panel to
/// reissue the identical request once the first operation finishes. It is not a
/// code for a busy host, a busy tool, a rate limit or any other retryable fault,
/// and the agent's four other locks WAIT rather than refuse, so none of them can
/// produce it.
///
/// The panel retries after a timeout, and a call that waited would hold an RPC
/// open for as long as the first backup runs — which for a twenty-gigabyte
/// account is measured in hours, not seconds — and would park a blocking-pool
/// thread for that whole time.
///
/// # Lock ordering: the whole graph, and why it has no cycle
///
/// Five locks exist in this workspace — the same five `rules/rust.md` lists
/// under "What this agent serialises", and this table is checked against that
/// one rather than remembered. Enumerated rather than asserted, because "there
/// is no deadlock" is a claim about every path and not about one:
///
/// | Lock | Scope | Acquisition |
/// |---|---|---|
/// | this one | per account | never blocks (`try_lock_owned`) |
/// | `crate::cron::cron_lock` | per account | waits |
/// | `crate::safe_write`'s config-tree lock | host-wide | waits |
/// | `crate::firewall`'s mutation lock | host-wide | waits |
/// | `crate::backup`'s `STDIN_IS_THE_ARTIFACT` | host-wide | waits |
///
/// The backup one is the narrowest and the least visible: it is held only for
/// the length of the fork that hands a dump's standard input to the archive, so
/// nothing else can claim that descriptor. It takes no other lock while it is
/// held, and it is entered by an operation that is already holding this one.
///
/// Every path that takes two, in the order it takes them:
///
/// - `AccountOperations::delete` → this lock, then `cron_lock` (around the
///   crontab removal), then released; and this lock, then the config-tree lock
///   (inside `remove_account_sftp`'s and `remove_account_pools`' config writes),
///   then released.
/// - `sftp::create_sftp_user` → this lock, then the config-tree lock (the
///   jail's mount unit goes through the config-write protocol).
/// - `ftps::create_ftps_user` → this lock, then the config-tree lock, for the
///   same reason and through the same protocol: `ensure_account_jail` writes the
///   FTPS jail's mount unit with `safe_write`.
/// - `sftp::set_sftp_password`, `ftps::set_ftps_password`,
///   `sftp::delete_sftp_user`, `ftps::delete_ftps_user` and
///   `logins::set_account_logins_locked` → this lock, and nothing else. They
///   spawn `getent`, `chpasswd`, `usermod` and `userdel` directly and enter no
///   config write.
/// - `php::write_pool` → this lock, then the config-tree lock (the pool goes
///   through the same config-write protocol). It is reached only from the site
///   operations, which hold no lock of their own, so this pair is the whole of
///   its ordering.
/// - the four backup operations → this lock, then, inside `create_backup`'s
///   own fork, `STDIN_IS_THE_ARTIFACT`. Nothing else.
///
/// No path takes them in any other order, and three facts make that a proof
/// rather than an inventory that must be re-checked by hand:
///
/// 1. **Nothing that holds a waiting lock asks for another one.** `cron_lock`
///    is taken as the first statement of a cron operation, which reaches
///    neither this area nor `safe_write`; the config-tree lock is taken INSIDE
///    the `safe_write` protocol, which calls no operation at all; the firewall
///    lock is taken by firewall mutations, which touch no account;
///    `STDIN_IS_THE_ARTIFACT` wraps one fork and calls no operation. All four
///    are leaves of the wait-for graph, so no cycle can pass through them.
/// 2. **This lock is never waited on.** A wait-for cycle needs every edge to be
///    a thread blocked while holding something; a `try_lock_owned` that fails
///    returns an error instead of blocking, so this lock contributes no edge.
/// 3. **Two per-account locks over one account is not a mistake here, it is the
///    reason the deletion can wait for cron without waiting for a backup.**
///    They are held for different things — this one for the account's existence
///    on the host, `cron_lock` for one spool file — and folding them into one
///    would give the account's lock cron's waiting semantics, which is precisely
///    the property fact 2 rests on.
///
/// **What this lock is not.** It is process-local, like the other four. It
/// serialises the RPCs of one running agent against each other and nothing
/// else: a second `maran-agent` binary an operator runs by hand, an installer
/// step, or a customer's own `crontab -e` contend with none of it. That is
/// sound for the product's threat model — one root daemon per host, systemd
/// managed, socket bound exclusively — and it is written down rather than left
/// to be assumed, because every re-read this seam performs (the account's
/// existence before `userdel`, the account's ids before a `useradd`, the
/// account's ids before a restore's home swap) exists precisely to catch what a
/// process-local lock cannot see.
///
/// The guard is owned, so a caller keeps it in a local and the lock is released
/// when the operation returns — by any path, including every error path and a
/// panic. There is no unlock to forget.
///
/// A poisoned registry lock is recovered from rather than propagated: it can
/// only be poisoned by a panic while a map lookup was in progress, the map is
/// not left inconsistent by that (a `HashMap` insert either happened or did
/// not), and a root daemon that refused every one of these operations for the
/// rest of its life over one unrelated panic would be a worse outcome than the
/// one being guarded against.
pub(crate) fn take_account_lock(account: &AccountName) -> Option<OwnedMutexGuard<()>> {
    let mut registry = ACCOUNT_LOCKS.lock().unwrap_or_else(PoisonError::into_inner);

    let locks = registry.get_or_insert_with(HashMap::new);
    let lock = Arc::clone(
        locks
            .entry(account.as_str().to_owned())
            .or_insert_with(|| Arc::new(AsyncMutex::new(()))),
    );

    // Dropped before the wait-free attempt below, so the registry is never held
    // across anything but its own lookup.
    drop(registry);

    lock.try_lock_owned().ok()
}

#[cfg(test)]
#[path = "../tests/accounts/account_lock_tests.rs"]
mod tests;
