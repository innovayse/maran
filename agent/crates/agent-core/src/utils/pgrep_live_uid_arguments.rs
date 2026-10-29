//! The argument list that asks whether any LIVE process of one uid exists.

/// The flag that makes `pgrep` print how many processes it matched.
const COUNT_FLAG: &str = "--count";

/// The flag that restricts the match to the listed process states.
const RUNSTATES_FLAG: &str = "--runstates";

/// Every process state except `Z` (zombie), as `pgrep --runstates` spells them.
///
/// `--runstates` takes an allow-list and has no negation, so the states are
/// named: uninterruptible sleep (`D`), running (`R`), interruptible sleep (`S`),
/// stopped (`T`), traced (`t`), paging (`W`), dead (`X`), idle kernel thread
/// (`I`) and parked (`P`).
///
/// A zombie is left out on purpose. It is a process that has already exited and
/// only waits for its parent to read the exit status: it holds no memory, no
/// file descriptor and no credential, cannot run code and cannot be signalled
/// to any effect, so it can neither act as the uid nor keep the uid's resources
/// alive. Counting it would refuse an account deletion for something no signal
/// can end, until a parent that may itself be wedged gets round to reaping it.
/// A live process in any listed state still counts, which is what the check
/// exists to catch.
const LIVE_STATES: &str = "D,R,S,T,t,W,X,I,P";

/// The flag that matches on the REAL uid, the same matcher the kill uses.
const UID_FLAG: &str = "--uid";

/// Builds the arguments (everything after the program) that make `pgrep` print
/// how many processes of real uid `uid` are alive, zombies excluded.
///
/// `uid` is a number rendered with `Display`, which has no room for a space, a
/// quote, a newline or a metacharacter; everything else is a constant. Exit
/// status follows `pkill`'s (0 matched, 1 nothing matched), so
/// [`PkillAnswer`](super::pkill_answer::PkillAnswer) reads it unchanged.
///
/// Unlike `pkill --signal 0` this sends nothing and, by design, does not see a
/// zombie; the kill half of a cull still uses `pkill_uid_arguments`.
#[must_use]
pub fn pgrep_live_uid_arguments(uid: u32) -> Vec<String> {
    vec![
        COUNT_FLAG.to_owned(),
        RUNSTATES_FLAG.to_owned(),
        LIVE_STATES.to_owned(),
        UID_FLAG.to_owned(),
        uid.to_string(),
    ]
}

#[cfg(test)]
#[path = "../tests/utils/pgrep_live_uid_arguments_tests.rs"]
mod tests;
