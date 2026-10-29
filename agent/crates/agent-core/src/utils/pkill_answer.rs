//! What `pkill`'s exit status says.

/// What `pkill` exits with when it matched at least one process and signalled
/// it.
const SIGNALLED: i32 = 0;

/// What `pkill` exits with when nothing matched.
///
/// Not a failure: an account with no live process is the ordinary case, and
/// both a cull and a check for survivors must treat it as success.
const NOTHING_MATCHED: i32 = 1;

/// The three things a `pkill` exit status can mean, and the only three this
/// agent acts on.
///
/// Every other status (2 for a usage error, 3 for a fatal one, a signal death
/// reported as a high number) is a refusal: the tool did not say whether
/// anything was signalled, so no caller may read it as "nothing was there".
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum PkillAnswer {
    /// At least one process matched and the signal was sent.
    Matched,
    /// No process matched.
    NothingMatched,
    /// `pkill` answered with some other status, carried here.
    Refused(i32),
}

impl PkillAnswer {
    /// Classifies a `pkill` exit status.
    #[must_use]
    pub fn from_status(status: i32) -> Self {
        match status {
            SIGNALLED => Self::Matched,
            NOTHING_MATCHED => Self::NothingMatched,
            other => Self::Refused(other),
        }
    }
}

#[cfg(test)]
#[path = "../tests/utils/pkill_answer_tests.rs"]
mod tests;
