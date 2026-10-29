//! Lists a backup artifact's members with `tar --list`, bounded on every side.

use std::path::Path;

use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::utils::child_input::ChildInput;
use maran_agent_core::utils::child_output::ChildOutput;
use maran_agent_core::utils::run_bounded::run_bounded;
use maran_agent_core::utils::spawn_request::SpawnRequest;
use maran_distro::AdapterBinary;

use crate::backup::backup_error::{BackupError, PROGRAM_UNAVAILABLE};
use crate::backup::bound_or::bound_or;

/// The flag that lists an archive's members without extracting anything.
const LIST: &str = "--list";

/// The flag that names the decompressor, by absolute path.
///
/// The same spelling the two argv builders use, and here for the same reason:
/// `--gzip` would have `tar` resolve a bare `gzip` through `PATH` in a child of
/// this root daemon. `--list` reads a customer-supplied artifact, so it gets
/// the same treatment as the extraction that follows it.
const COMPRESS_PROGRAM: &str = "--use-compress-program=";

/// The flag naming the archive file to read.
const FILE: &str = "--file";

/// The most standard output `tar --list` may write before the listing is
/// refused: 256 MiB.
///
/// The listing is read whole into this root process and then split into one
/// owned `String` per member, so the memory it costs is about 2.3 times this
/// figure: the text is kept whole and one owned `String` per member is built
/// beside it (about 590 MiB at this cap and `MAXIMUM_MEMBERS` together). The
/// unit sets no `MemoryMax`, so on a small host this is bounded but not small.
/// The cap is not lowered to spare it, because a lower figure would make restore
/// refuse what creation can write; streaming the pre-scan so no listing is
/// held is the structural fix and needs a streaming API in the core. Past it the core kills the archiver's whole process group and
/// returns an error; no member read so far is handed on, because the pre-scan
/// decides on the WHOLE listing and a listing cut short would let a hostile
/// member placed after the cut through to the extraction (rules/security.md,
/// and `scan_members`).
///
/// **How it relates to what `CreateBackup` can produce.** Creation bounds the
/// archive's size in bytes (64 GiB) and not its member count, so no figure
/// here can be proved to be at least what creation can write: a home holding
/// tens of millions of tiny files compresses to far less than that ceiling and
/// lists to more than this one. 256 MiB is about two million members at the
/// hundred-byte average path, which is far above any home this panel's plans
/// describe. A home above it is refused when the backup is CREATED:
/// `create_backup` refuses a home whose walk counts more members than
/// `MAXIMUM_MEMBERS` before any work begins, and `create_archive` then lists the
/// archive it has just written with these same bounds (the authority, and the
/// only check on this byte cap), so no backup made from now on can be
/// unrestorable for its size.
/// Backups written by an earlier release are not re-checked; one above this
/// figure is refused at restore with an explicit bound, never a truncated scan
/// (recorded in the threat note).
const LISTING_STDOUT_CAP: u64 = 256 * 1024 * 1024;

/// The most standard error `tar --list` may write: 1 MiB.
///
/// Nothing reads it (a refusal is reported by exit status alone, because the
/// archive is customer-supplied and `tar` quotes member names back). It is
/// captured only because the core captures both streams or neither; a hostile
/// archive that makes `tar` warn about every member hits this cap and is
/// refused, which is the right outcome for it.
const LISTING_STDERR_CAP: u64 = 1024 * 1024;

/// The most members a listing may hold before the archive is refused.
///
/// See `LISTING_STDOUT_CAP` for how this relates to what creation can
/// produce. Counting members, and not only bytes, bounds the number of owned
/// strings the root process allocates for a listing made of very short names.
pub(super) const MAXIMUM_MEMBERS: usize = 2_000_000;

/// The longest member name, as `tar --list` prints it, that is accepted:
/// 16 KiB.
///
/// A path on this system is at most 4096 bytes (`PATH_MAX`), so no name that
/// `CreateBackup` writes from a real directory tree is longer than that; and
/// `tar` prints a non-ASCII byte under the C locale this child runs in as a
/// four-character octal escape, so the printed form of such a name is at most
/// four times as long. A longer name could only have been put there by
/// someone who built the archive by hand.
const MAXIMUM_NAME_BYTES: usize = 4 * 4096;

/// The `status` reported when the listing was refused for its shape: too many
/// members, or a name too long.
///
/// Negative for the reason `ARCHIVER_REFUSED_UNDER_ACCOUNT` (in the process host) is, and a third
/// value because it says a third thing: `tar` ran and exited cleanly, and this
/// agent refused what it printed.
pub(super) const LISTING_REFUSED: i32 = -3;

/// Lists `artifact`'s members with `tar --list`, extracting nothing.
///
/// The names come back exactly as the archive stores them. Nothing is
/// trimmed, unescaped or normalised here: this output is the pre-scan's
/// evidence, and a host that tidied a `..` away would be deciding the
/// question the scan exists to ask.
///
/// # The listing is bounded, and a bound refuses the archive
///
/// The archive is customer-supplied and this runs as root, so every
/// dimension of the answer is bounded and none of the bounds truncates:
///
/// - **Time:** the [`CallClass::Listing`] deadline (one hour). A gzip stream
///   that expands enormously costs one core until then, after which `tar` and
///   its compressor are killed together as one process group. The listing is
///   bounded by the artifact ceiling and not by the customer's home, which is
///   why it is not [`CallClass::Bulk`]: a bomb must not hold the backup lock
///   for twelve hours.
/// - **Bytes:** `LISTING_STDOUT_CAP` of standard output.
/// - **Members:** `MAXIMUM_MEMBERS`.
/// - **One name:** `MAXIMUM_NAME_BYTES`.
///
/// Reaching any of them returns an error and NEVER the members read so
/// far. The result is the pre-scan's evidence and it decides on the whole
/// listing: a prefix handed on as if complete would let a hostile member
/// placed after the cut reach the extraction with only `tar`'s own
/// stripping left between it and the disk.
///
/// # Errors
///
/// [`BackupError::ArchiveFailed`], including for an artifact that is not a
/// readable gzip stream at all, and with `LISTING_REFUSED` when the
/// listing has more members than `MAXIMUM_MEMBERS` or a name longer than
/// `MAXIMUM_NAME_BYTES`. [`BackupError::Bound`] when the deadline passed
/// or either output stream went past its cap.
pub(super) fn list_archive_members(
    tar: &AdapterBinary,
    gzip: &AdapterBinary,
    artifact: &Path,
) -> Result<Vec<String>, BackupError> {
    let compressor = format!("{COMPRESS_PROGRAM}{gzip}");
    let artifact_text = artifact.to_string_lossy();
    let borrowed: Vec<&str> = vec![LIST, compressor.as_str(), FILE, &*artifact_text];

    let outcome = run_bounded(SpawnRequest {
        program: tar.as_path(),
        arguments: &borrowed,
        class: CallClass::Listing,
        input: ChildInput::Null,
        output: ChildOutput::Capture {
            stdout_cap: LISTING_STDOUT_CAP,
            stderr_cap: LISTING_STDERR_CAP,
        },
    })
    .map_err(|error| {
        bound_or(
            error,
            BackupError::ArchiveFailed {
                status: PROGRAM_UNAVAILABLE,
            },
        )
    })?;
    if outcome.status != 0 {
        return Err(BackupError::ArchiveFailed {
            status: outcome.status,
        });
    }

    bounded_members(&outcome.stdout, MAXIMUM_MEMBERS, MAXIMUM_NAME_BYTES)
}

/// Splits a `tar --list` listing into members, refusing it whole when it is
/// past either bound.
///
/// A separate function taking the bounds as arguments so the refusal can be
/// exercised at small sizes. Reaching a bound returns an error and never the
/// members read so far: the pre-scan decides on the WHOLE listing.
///
/// # Errors
///
/// [`BackupError::ArchiveFailed`] with `LISTING_REFUSED` when the listing has
/// more than `max_members` members or a name longer than `max_name_bytes`.
fn bounded_members(
    listing: &str,
    max_members: usize,
    max_name_bytes: usize,
) -> Result<Vec<String>, BackupError> {
    let mut members = Vec::new();
    for line in listing.lines().filter(|line| !line.is_empty()) {
        if members.len() >= max_members || line.len() > max_name_bytes {
            return Err(BackupError::ArchiveFailed {
                status: LISTING_REFUSED,
            });
        }
        members.push(line.to_owned());
    }

    Ok(members)
}

#[cfg(test)]
#[path = "../tests/backup/list_archive_members_tests.rs"]
mod tests;
