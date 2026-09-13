//! Turning a caller-supplied name into one an `*at` syscall may be given.

use std::ffi::{CString, OsStr};
use std::io;
use std::os::unix::ffi::OsStrExt;

/// Converts `name` into a C string, refusing anything that is not a single
/// entry name inside the directory the caller holds open.
///
/// Every `*at` syscall in this module resolves its name RELATIVE to the
/// directory descriptor it is given, so a name of `../../etc/shadow` walks
/// straight out of the directory the descriptor was supposed to pin. The
/// descriptor closes the race on the components ABOVE the name; this closes the
/// name itself, and it is the reason a caller may treat "I hold this directory"
/// as "I cannot be steered elsewhere".
///
/// Extracted rather than repeated: five wrappers here take an entry name, and a
/// check copied five times is a check that is four edits away from disagreeing
/// with itself. `open_in_directory` carried the only copy, so the four wrappers
/// added for the challenge write would each have had to reproduce it from
/// memory.
///
/// Refuses an empty name, a name containing `/`, the names `.` and `..`, and a
/// name containing an interior NUL. `/` is refused rather than merely `..`
/// because a multi-component name is a path, and this function's whole promise
/// is that its result is not one.
///
/// # Errors
///
/// Returns [`io::ErrorKind::InvalidInput`] for every rejection. The reasons are
/// not distinguished: they are all "the caller passed something that is not an
/// entry name", none of them can happen for a name derived from a validated
/// type, and a caller that got one wrong has a bug rather than a choice to make.
pub fn directory_entry_name(name: &OsStr) -> io::Result<CString> {
    let bytes = name.as_bytes();
    if bytes.is_empty() || bytes.contains(&b'/') || bytes == b"." || bytes == b".." {
        return Err(io::Error::from(io::ErrorKind::InvalidInput));
    }

    CString::new(bytes).map_err(|_| io::Error::from(io::ErrorKind::InvalidInput))
}

#[cfg(test)]
#[path = "../tests/privs/directory_entry_name_tests.rs"]
mod tests;
