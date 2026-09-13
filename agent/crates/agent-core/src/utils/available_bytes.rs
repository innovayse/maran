//! Asking a filesystem how much room it has, at the path a write is about to
//! land on.

use std::io::{Error, ErrorKind};
use std::path::Path;

/// Bytes an unprivileged writer could still add to the filesystem `directory`
/// lives on.
///
/// **The argument is the directory the write actually goes to, never a
/// configured string and never its parent.** That is the whole discipline this
/// helper exists to make cheap: a parent is not necessarily the same mount, and
/// on this product it very often is not — `/run/maran/scratch` and
/// `/var/lib/maran-scratch` are both directories this agent stages files in and
/// sit on filesystems whose sizes differ by two orders of magnitude. A ceiling
/// computed against the wrong mount is a ceiling that reports on a filesystem
/// nothing is being written to, which is the failure `rules/testing.md` calls a
/// check that cannot observe what it claims. The directory must therefore
/// already exist when this is called; a caller that has not created its scratch
/// yet is asking about a mount it has not reached.
///
/// # Why AVAILABLE and not free
///
/// `statvfs` answers with three block counts and only one of them is a promise.
/// `f_bavail` — the count used here — excludes the reserve that ext4 and
/// friends keep for root, typically 5%. The agent IS root and could write into
/// that reserve, so this number is deliberately pessimistic for its own
/// caller: the reserve is what the rest of the machine has left to fail
/// gracefully with when a filesystem fills, and a backup is precisely the
/// workload that must not be the thing which eats it. Refusing a few percent
/// early costs an operator a message; spending the reserve costs them a host
/// on which nothing else can write either.
///
/// Blocks are multiplied by the FRAGMENT size, `f_frsize`, because POSIX
/// defines all three counts in units of that field and not of the preferred
/// block size. The two are equal on every filesystem this product will meet,
/// which is exactly why using the other one would be a defect that hides until
/// the day they are not.
///
/// # Errors
///
/// Returns the underlying error when the path cannot be queried — it does not
/// exist, a component is not a directory, or it is not reachable. A caller must
/// treat that as "unknown", never as "plenty": a ceiling that falls back to a
/// generous default when it could not measure is a ceiling that disappears
/// exactly when the filesystem is unhealthy.
pub fn available_bytes(directory: &Path) -> Result<u64, Error> {
    let space = rustix::fs::statvfs(directory).map_err(Error::from)?;

    space
        .f_bavail
        .checked_mul(space.f_frsize)
        .ok_or_else(|| Error::new(ErrorKind::InvalidData, "filesystem size overflows u64"))
}

#[cfg(test)]
#[path = "../tests/utils/available_bytes_tests.rs"]
mod tests;
