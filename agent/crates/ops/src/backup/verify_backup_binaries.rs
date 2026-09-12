//! The check that a scheduled backup, run unattended, cannot perform for
//! itself before it is already the operation that needed the answer.

use maran_distro::DistroAdapter;

use crate::backup::backup_error::BackupError;
use crate::backup::executable_lookup::ExecutableLookup;

/// Confirms that `tar`, `gzip` and the database dump client this family's
/// adapter names are all present and executable.
///
/// # Why this exists, and where it does and does not run
///
/// `tar_binary`, `gzip_binary` and `database_dump_binary` are `&'static str`
/// literals measured once, on two polygon images, on one date (2026-09-05).
/// Nothing
/// re-asks the question on a real, running host: a package removed, renamed,
/// or a compatibility symlink an update drops changes nothing this agent has
/// checked, so the first thing that would notice is a failed scheduled backup
/// at 03:00 — `create_backup`'s own `DumpFailed`/`ArchiveFailed` with
/// `PROGRAM_UNAVAILABLE`, which is loud, but only at the moment the operation
/// that most needs to have already worked discovers it has not.
///
/// This function is the belated answer, called at agent startup
/// (`server::serve`) against the real filesystem, logged as a warning and
/// never as a reason to refuse to start: the backup subsystem is not wired
/// into the service registry yet, and a daemon that already carries cron,
/// firewall and monitoring behind the same socket must not go down over a
/// dependency none of those three subsystems touches. A hard refusal here
/// would trade an unused subsystem's absence for every OTHER subsystem's
/// availability, which is the wrong side of that trade.
///
/// # What this still cannot see
///
/// A check at startup answers the question **at that moment**. The agent is a
/// long-running daemon; a package manager can remove or replace any of these
/// three binaries at any point after this line has run, and nothing here
/// re-asks the question before the next scheduled backup. Closing that
/// requires the same call immediately before a backup or restore actually
/// spawns one of these three programs — the "immediately before the
/// operation" seam `create_backup.rs`/`process_backup_host.rs` own, which is
/// occupied by concurrent work at the time this check was written and is not
/// touched here. Startup answers "was it true when the daemon last came up";
/// only a pre-run check answers "is it true right now".
///
/// # Errors
///
/// [`BackupError::BackupBinaryMissing`] naming the first program that is not
/// present and executable, and the exact path the adapter declared for it —
/// not "a required program is missing", which sends an operator hunting
/// through three candidates and two families.
pub fn verify_backup_binaries(
    distro: &dyn DistroAdapter,
    lookup: &dyn ExecutableLookup,
) -> Result<(), BackupError> {
    let candidates: [(&'static str, &'static str); 3] = [
        ("tar", distro.tar_binary()),
        ("gzip", distro.gzip_binary()),
        ("database dump client", distro.database_dump_binary()),
    ];

    for (program, path) in candidates {
        if !lookup.is_executable(path) {
            return Err(BackupError::BackupBinaryMissing {
                program: program.to_string(),
                path: path.to_string(),
            });
        }
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/verify_backup_binaries_tests.rs"]
mod tests;
