//! What a spawned child reads from its standard input.

use core::fmt;
use std::fs::File;

/// What the child's standard input is, stated by the caller every time.
///
/// There is no "inherit" variant, on purpose. `Command::output()` gave every
/// child a closed input, but `Command::spawn()` inherits the daemon's own, and
/// during an account-side archive extraction the daemon's descriptor 0 IS the
/// customer's archive: a core that forgot to say `null` would hand that
/// customer's bytes to whichever root program another thread started in the
/// same window. A caller that really needs to pass the daemon's own input on
/// (the extraction inside its fork) uses `run_unthreaded`, which takes a
/// `Stdio` for exactly that reason.
pub enum ChildInput<'a> {
    /// `/dev/null`: a tool that decides to prompt reads end-of-file and fails
    /// instead of hanging a root daemon.
    Null,
    /// These bytes, then end-of-file. Written by a thread of the core, and
    /// copied once for that thread; the copy is overwritten once it has been
    /// written. Used for a password or a private key that must never reach a
    /// command line.
    Bytes(&'a [u8]),
    /// This file, already opened by the caller.
    File(File),
}

impl fmt::Debug for ChildInput<'_> {
    /// Writes the variant and, for bytes, their count — never a byte.
    ///
    /// Written by hand because the derived `Debug` would print the slice, and
    /// the slice is a credential for every caller that uses it.
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Null => formatter.write_str("ChildInput::Null"),
            Self::Bytes(bytes) => formatter
                .debug_struct("ChildInput::Bytes")
                .field("len", &bytes.len())
                .finish(),
            Self::File(_) => formatter.write_str("ChildInput::File"),
        }
    }
}

#[cfg(test)]
#[path = "../tests/utils/child_input_tests.rs"]
mod tests;
