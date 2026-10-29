//! Ending the transfer sessions a suspended account already has open.

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::pkill_answer::PkillAnswer;
use maran_agent_core::utils::pkill_uid_arguments::pkill_uid_arguments;
use maran_agent_core::validation::system::managed_account::ManagedAccount;
use maran_distro::DistroAdapter;

use crate::logins::logins_error::LoginsError;
use crate::logins::logins_host::LoginsHost;

/// The signal itself.
///
/// `KILL` and not `TERM`, deliberately, and the cost is stated where an operator
/// reads it (`docker/README.md`): a session being ended is a transfer being cut
/// either way, so the only thing a graceful signal buys is the chance that the
/// session process ignores it — and a session that survives a suspension is the
/// entire defect this operation exists to close. The alternative, TERM then a
/// wait then KILL, would put a sleep inside a root daemon's critical section and
/// would still have to observe the result through a second `pkill` that cannot
/// distinguish a live process from a killed one nobody has reaped yet.
const KILL_SIGNAL: &str = "KILL";

/// Sends a fatal signal to every process running as `account`'s uid, and answers
/// with how many there were.
///
/// This is what makes a suspension stop access rather than only stop new
/// logins. The marker a suspension writes into the shadow field is consulted at
/// AUTHENTICATION, and neither sshd nor vsftpd re-authorises a session it has
/// already admitted — so before this existed, a suspended customer's open
/// session kept reading and writing their home: on FTPS until it had been idle
/// for ten minutes, on SFTP for as long as they cared to hold it, and on either
/// protocol for as long as a transfer kept running.
///
/// Threat note: `docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`.
/// **Second reviewer: OUTSTANDING** (rules/security.md "Sensitive change
/// escalation").
///
/// # How the uid is confined
///
/// `pkill --uid <n>` matches on the real uid, so the kernel — not this code —
/// is what keeps a process that is not running as `<n>` out of the candidate
/// set. The whole question is therefore where `<n>` comes from, and it comes
/// from exactly one place: the [`ManagedAccount`] the caller hands in, which
/// the managed-account type built from the password row whose name EQUALS the
/// validated account name. No name is parsed to derive it and nothing from the
/// request reaches the argv.
///
/// This function keeps no copy of the rules that decide which accounts are
/// managed. The type refuses a reserved name, an id below the distro's floor
/// (which includes uid 0) and a home other than the one this agent gives a
/// hosting account, so an operator's own login (`ubuntu`, a human administrator)
/// and every system user are refused before this function can be called with
/// them. A second private copy of those rules here was the defect: it refused
/// uid 0 and a foreign home and nothing else, so it culled an ordinary login
/// whose home happened to be `/home/<name>`.
///
/// # What it cannot become
///
/// A way to run something. The program is `distro.pkill_binary()` — `ops` names
/// no platform literal (rules/rust.md "Distro adapter", enforced by
/// `maran structure`) — every other argument is a `const` in this file, and the
/// uid is a `u32` rendered with `Display`, which has no room for a space, a
/// quote, a newline or a metacharacter. [`LoginsHost::run`] spawns an argv array
/// through `execve`, so there is no command line for anything to re-parse
/// (rules/security.md items 3 and 12).
///
/// # What it costs the customer
///
/// An upload in flight is cut at whatever byte it had reached, so the account's
/// home can be left holding a partial file. Nothing deletes it and nothing marks
/// it: this agent cannot tell a truncated upload from a file the customer meant
/// to write, and deleting a customer's data on a suspension would be a larger
/// hazard than the short file. The sentence an operator should read is in
/// `docker/README.md`.
///
/// # Which sessions
///
/// Every process of the uid, idle or transferring. Culling only the busy ones
/// would mean classifying processes by name or state, which is a guess this code
/// would then be believed about — and the idle case is the one FTPS bounds at
/// ten minutes and SFTP does not bound at all, so ten retained minutes on one
/// protocol and unbounded retention on the other is precisely what an abuse
/// suspension cannot accept. `idle_session_timeout=600` stays where it is: it
/// still bounds the idle sessions of every account this cull never touches.
///
/// # The lock
///
/// This function does NOT take a lock, and must not be called without one. Its
/// only caller holds the hosting account's lock across the enumeration, the
/// locking and this cull, which is what lets this operation assume that no
/// `fork_as_account` child of this agent is running as the account's uid — such
/// a child would otherwise be killed mid-write by our own `pkill`. That lock is
/// process-local and excludes nothing outside this binary.
///
/// # Errors
///
/// - [`LoginsError::SpawnFailed`] when `pkill` could not be started at all.
/// - [`LoginsError::Bound`] when `pkill` outran its deadline. How many processes
///   were signalled is then unknown, which is why this is an error and not zero.
/// - [`LoginsError::SessionCullFailed`] when `pkill` answered with anything but
///   "signalled" or "nothing matched". Some of the account's processes may have
///   been signalled and some may not, which is why this is an error and not a
///   count: the caller must not report a suspension it cannot account for.
pub(crate) fn end_account_sessions(
    host: &dyn LoginsHost,
    distro: &dyn DistroAdapter,
    account: &ManagedAccount,
) -> Result<u32, LoginsError> {
    // The matcher is `pkill_uid_arguments`, the one spelling shared with account
    // deletion's process cull; only the signal is this operation's own.
    let arguments = pkill_uid_arguments(KILL_SIGNAL, account.uid());
    let arguments: Vec<&str> = arguments.iter().map(String::as_str).collect();
    let outcome = host.run(&distro.pkill_binary(), &arguments, CallClass::Mutation)?;

    match PkillAnswer::from_status(outcome.status) {
        PkillAnswer::Matched => Ok(signalled_count(&outcome.stdout)),
        PkillAnswer::NothingMatched => Ok(0),
        PkillAnswer::Refused(code) => Err(LoginsError::SessionCullFailed { code }),
    }
}

/// How many processes `pkill --count` said it matched.
///
/// An unreadable number is reported as zero rather than as an error, and that is
/// the one place this operation prefers an understatement to a refusal: the
/// processes HAVE been signalled by the time this is parsed — the count is
/// `pkill`'s report of work it already did — so failing here would fail a
/// suspension whose cull succeeded. The number is an operator-facing figure, not
/// a decision: nothing branches on it.
fn signalled_count(stdout: &str) -> u32 {
    stdout
        .lines()
        .next()
        .unwrap_or_default()
        .trim()
        .parse()
        .unwrap_or(0)
}

#[cfg(test)]
#[path = "../tests/logins/end_account_sessions_tests.rs"]
mod tests;
