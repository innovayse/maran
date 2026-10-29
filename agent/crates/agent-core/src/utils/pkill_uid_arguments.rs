//! The argument list that sends one signal to every process of one uid.

/// The flag naming the signal to send, spelled long so it cannot be read as a
/// pattern.
const SIGNAL_FLAG: &str = "--signal";

/// The flag that makes `pkill` print how many processes it matched.
///
/// Without it a caller would know only that `pkill` was content, and "the
/// signal did something" and "there was nothing to signal" would be the same
/// answer.
const COUNT_FLAG: &str = "--count";

/// The flag that matches on the REAL uid.
///
/// `--uid` and not `--euid`: a process that dropped to an account permanently
/// has a real uid the kernel will not let it change back, so it is the uid a
/// confined process cannot shed.
const UID_FLAG: &str = "--uid";

/// Builds the arguments (everything after the program) that make `pkill` send
/// `signal` to every process whose real uid is `uid`, and print how many it
/// matched.
///
/// `signal` is a name or number that the CALLER writes as a constant (`KILL`,
/// `0`); it is never request data. `uid` is a number rendered with `Display`,
/// which has no room for a space, a quote, a newline or a metacharacter. No
/// end-of-options marker follows, because the uid is the argument of `--uid`
/// and no operand comes after it, so there is nothing for a marker to protect.
///
/// Signal `0` sends nothing and only reports whether a matching process exists,
/// which is how a caller asks "is anything still running as this uid" with the
/// same matcher that signalled.
#[must_use]
pub fn pkill_uid_arguments(signal: &str, uid: u32) -> Vec<String> {
    vec![
        SIGNAL_FLAG.to_owned(),
        signal.to_owned(),
        COUNT_FLAG.to_owned(),
        UID_FLAG.to_owned(),
        uid.to_string(),
    ]
}

#[cfg(test)]
#[path = "../tests/utils/pkill_uid_arguments_tests.rs"]
mod tests;
