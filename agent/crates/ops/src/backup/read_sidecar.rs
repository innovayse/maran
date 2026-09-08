//! Reading a sidecar off disk, before anything trusts what it says.

use std::path::Path;

use crate::backup::model::backup_state::BackupState;
use crate::backup::model::backup_summary::{BackupSummary, SUMMARY_VERSION};
use crate::backup::model::readable_backup::ReadableBackup;
use crate::backup::model::unreadable_reason::UnreadableReason;

/// Reads and parses the sidecar at `path`, refusing one that names a
/// [`SUMMARY_VERSION`] this agent does not understand.
///
/// This is the ONE place a sidecar's bytes are turned into something this
/// crate will go on to trust — mirroring
/// [`read_manifest`](crate::backup::archive::read_manifest::read_manifest),
/// which does the same thing for the archive's own manifest and for the same
/// reason. Both of this crate's readers of a sidecar —
/// [`list_backups`](crate::backup::list_backups) reporting a foreign-version
/// entry as [`UnreadableReason::UnknownVersion`] rather than corrupt, and
/// `restore_backup`'s cross-check against the archive's manifest — go through
/// this function rather than each parsing the file and checking the version
/// (or not) on its own: a second copy of "parse, then check the version" is
/// exactly the shape that let the version check exist on one side and not the
/// other, which is the defect this function closes. A future third reader
/// gets the check for free by construction, because there is no shorter way
/// to read a sidecar than calling this.
///
/// **It answers a [`ReadableBackup`], not a [`BackupSummary`].** A caller that
/// receives `Ok` from here holds the manifest, the size and the digest
/// themselves — there is no readable-looking value with a `None` where the
/// digest should be for it to have to re-check, because the type it is handed
/// cannot be built without them. A sidecar that parses but describes ITSELF as
/// unreadable — a shape this agent never writes, and therefore one somebody
/// else placed — is refused here with its own recorded reason rather than
/// passed on for each caller to notice separately.
///
/// A sidecar that is missing, or whose JSON does not parse into any shape
/// this agent has ever written, is [`UnreadableReason::Corrupt`] — the same
/// bucket [`BackupSummary::unreadable`] reports, and deliberately not
/// distinguished further here: a caller that cares why parsing failed has
/// nothing more useful to do with the distinction than a caller that only
/// needs to know it did. A sidecar written in the flat, pre-[`BackupState`]
/// shape falls in this bucket too, for the reason [`SUMMARY_VERSION`] states.
///
/// # Errors
///
/// [`UnreadableReason::Corrupt`] when the file cannot be read or its JSON
/// will not parse into a [`BackupSummary`]; [`UnreadableReason::UnknownVersion`]
/// when it parses but [`BackupSummary::version`] is not [`SUMMARY_VERSION`];
/// and the document's own reason when it parses at the right version and
/// describes an unreadable backup.
pub(crate) fn read_sidecar(path: &Path) -> Result<ReadableBackup, UnreadableReason> {
    let text = std::fs::read_to_string(path).map_err(|_| UnreadableReason::Corrupt)?;
    let summary: BackupSummary =
        serde_json::from_str(&text).map_err(|_| UnreadableReason::Corrupt)?;

    if summary.version != SUMMARY_VERSION {
        return Err(UnreadableReason::UnknownVersion {
            version: summary.version,
        });
    }

    match summary.state {
        BackupState::Readable(details) => Ok(details),
        BackupState::Unreadable(reason) => Err(reason),
    }
}

#[cfg(test)]
#[path = "../tests/backup/read_sidecar_tests.rs"]
mod tests;
