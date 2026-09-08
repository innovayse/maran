//! The chain check both bulk operations run before they stage anything in the
//! root-only scratch.
//!
//! It lives beside its two callers rather than inside either of them because
//! creation and restore stage into the same tree, under the same root, and a
//! boundary enforced by one of them and not the other is not a boundary at
//! all: a restore writes a customer's plaintext dumps into exactly the
//! directory the creation side proved was root's. Two copies of a security
//! check drift, and the one that drifts is the one nobody was reading.

use std::os::unix::fs::MetadataExt as _;
use std::path::Path;

use crate::backup::backup_error::BackupError;

/// The mode the scratch root and everything under it is created with, and the
/// only one any of it may carry.
pub(crate) const SCRATCH_MODE: u32 = 0o700;

/// The bits that may not be set on a directory ABOVE the scratch root: group
/// write and other write. A directory somebody else can write is a directory
/// somebody else can put the next name down into.
const OTHERS_WRITE_BITS: u32 = 0o022;

/// Whether a level of the chain that does not exist yet is a refusal.
#[derive(Clone, Copy, PartialEq, Eq)]
pub(crate) enum MissingLevels {
    /// Before creation: a level that is not there yet is not a finding, and the
    /// levels that ARE there still have to be sound.
    Tolerated,
    /// After creation: every level must exist and be stateable. A level that
    /// cannot be stated is reported as a refusal rather than passed over —
    /// this check says what it could observe, and it observed nothing.
    Refused,
}

/// States every directory from `base` down to `leaf`, and refuses the operation
/// unless each one is a real directory owned by `owner` that nobody else can
/// write.
///
/// Two rules, because the chain has two halves and they are not held to the
/// same standard:
///
/// - **Above `root`** — `/`, `/var`, `/var/lib` in production — the rule is
///   that they are root's and are not group- or other-WRITABLE. They are
///   distribution-owned directories that must stay readable and traversable by
///   everybody, so demanding `0700` of them would be demanding a broken system.
///   Write is the bit that matters: it is what lets somebody put an entry at
///   the next name down.
/// - **`root` and below** — this agent creates them and nothing else has any
///   business in them, so the rule is exactly [`SCRATCH_MODE`], with no group
///   bit, no other bit and none of setuid/setgid/sticky.
///
/// `symlink_metadata` and not `metadata` throughout: the question is what each
/// level IS. `metadata` follows a symlink, so a link pointing at a root-owned
/// directory would be approved on the strength of its target while the write
/// went wherever its author later re-pointed it — which is the exact shape of
/// the measured escalation.
///
/// # Errors
///
/// [`BackupError::ScratchUnusable`], including when `base` is not an ancestor
/// of `leaf`: a walk that cannot reach its leaf has checked nothing, and
/// answering `Ok` there would be a gate reporting on a path it never visited.
pub(crate) fn require_root_only_chain(
    base: &Path,
    root: &Path,
    leaf: &Path,
    owner: u32,
    missing: MissingLevels,
) -> Result<(), BackupError> {
    if !leaf.starts_with(base) {
        return Err(BackupError::ScratchUnusable);
    }

    let mut levels: Vec<&Path> = leaf
        .ancestors()
        .take_while(|level| *level != base)
        .collect();
    levels.push(base);

    for level in levels.into_iter().rev() {
        let metadata = match level.symlink_metadata() {
            Ok(metadata) => metadata,
            Err(_) if missing == MissingLevels::Tolerated => continue,
            Err(_) => return Err(BackupError::ScratchUnusable),
        };

        if !metadata.is_dir() || metadata.uid() != owner {
            return Err(BackupError::ScratchUnusable);
        }

        let mode = metadata.mode() & 0o7777;
        let forbidden = if level.starts_with(root) {
            // Everything but the owner's own rwx.
            0o7777 & !SCRATCH_MODE
        } else {
            OTHERS_WRITE_BITS
        };
        if mode & forbidden != 0 {
            return Err(BackupError::ScratchUnusable);
        }
    }

    Ok(())
}
