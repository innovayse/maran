//! What one finished backup is, to a caller, to the sidecar on disk, and to a
//! listing that cannot read that sidecar.

use serde::{Deserialize, Serialize};

use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::backup_state::BackupState;
use crate::backup::model::readable_backup::ReadableBackup;
use crate::backup::model::unreadable_reason::UnreadableReason;

/// The sidecar version this agent writes, and the only one it trusts the body
/// of.
///
/// Carried on [`BackupSummary`] itself rather than on a separate on-disk
/// type, unlike [`BackupManifest`], whose version lives on a document nothing
/// else in the workspace parses directly. `BackupSummary` does not have that
/// luxury: it is already the exact type `restore_backup` deserialises the
/// sidecar into to cross-check the archive's own manifest, so a second type
/// for "the sidecar on disk" would need to be kept byte-for-byte in step with
/// this one by convention alone — the precise hazard this type's own doc
/// already rejects a second type over ("two types would be two chances for
/// the answers to drift"). One type, one version field on it, is the
/// narrower risk.
///
/// The argument for refusing an unknown version rather than guessing at it is
/// made in full on [`MANIFEST_VERSION`](crate::backup::model::backup_manifest::MANIFEST_VERSION)
/// and is not repeated here: it is the same argument, for the same reason, in
/// the same folder.
///
/// **The number is not bumped by the shape change that introduced
/// [`BackupState`].** A version number is only consulted after the document
/// has parsed, so it cannot be the thing that recognises a document of the
/// PREVIOUS shape: a sidecar written flat (`"readable": true` beside optional
/// `manifest`, `artifact_bytes` and `artifact_sha256` keys) no longer parses
/// into this type at all and is reported as
/// [`UnreadableReason::Corrupt`] — visible, named, and prunable, which is the
/// outcome this module requires of anything it cannot read. Bumping the
/// number would change nothing about that and would only claim a
/// distinction the parser cannot draw. This is affordable exactly once,
/// before the first release; after it, a shape change needs a version read
/// before the body is, which this document does not have.
pub const SUMMARY_VERSION: u32 = 1;

/// A finished backup: its id, and either everything its sidecar said or the
/// reason nothing it said could be trusted.
///
/// This is also, byte for byte, what the sidecar `<id>.meta.json` holds. One
/// type and not two, because a listing that reads the sidecar and a creation
/// that returns to its caller are answering the same question, and two types
/// would be two chances for the answers to drift.
///
/// # The readable case is a shape, not a flag
///
/// [`Self::state`] is a [`BackupState`] rather than a `readable: bool` beside
/// three `Option` fields, because this type is built by `serde` from a file
/// this agent may not have written, and a promise the constructors keep is
/// not a promise a parser keeps. The flat shape allowed
/// `{"version":1,"readable":true}` — readable, no manifest, no digest — to
/// deserialise cleanly and be reported to the panel as a healthy backup, with
/// a `None` where the digest a restore checks the artifact against should
/// be. In the shape here that document does not parse: the readable state IS
/// a [`ReadableBackup`], and a `ReadableBackup` with no manifest cannot be
/// constructed by any means, `serde` included.
///
/// **The unreadable case is a variant of this type, never an omission from a
/// list of it.** A listing that only ever returns entries it could parse looks
/// correct right up until a disk fills with sidecars nothing will ever delete:
/// a corrupt or missing sidecar makes its backup invisible to retention, which
/// counts what it can see and prunes the oldest of that — an entry retention
/// cannot see is an entry it will never prune. So
/// [`list_backups`](crate::backup::list_backups) reports every artifact it
/// finds, readable or not, and a caller that only wants the readable ones
/// filters on [`Self::is_readable`] itself, in the open, rather than the
/// filtering happening here where nothing after it could notice.
///
/// Which situation an unreadable entry is in — damaged, foreign-versioned, or
/// not a regular file at all — is [`UnreadableReason`]'s to say, and that
/// type's doc says why the distinctions are worth keeping apart.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BackupSummary {
    /// The backup's id — the artifact's file name stem, always known because
    /// it is read from the artifact's own name, never from the sidecar that
    /// might be missing.
    ///
    /// Always a [`BackupId`](maran_agent_core::validation::system::backup_id::BackupId)'s
    /// text: [`list_backups`](crate::backup::list_backups) refuses a
    /// directory holding an artifact name it cannot parse as one rather than
    /// reporting the name here, so nothing downstream ever receives a
    /// file name in this field.
    ///
    /// Defaulted when a sidecar does not carry one, because the sidecar's own
    /// copy is never the answer anyway: a listing overwrites it with the
    /// artifact's name before the value leaves the function.
    #[serde(default)]
    pub backup_id: String,

    /// The sidecar layout version. [`SUMMARY_VERSION`] when this agent wrote
    /// it; `0` when the document did not name one this agent understands.
    ///
    /// Never consulted by a caller that already has a [`Self`] in hand —
    /// [`Self::state`] already says everything the version could add. It
    /// exists so the one function that parses a sidecar has something to
    /// check the document's claim against before trusting the rest of it.
    #[serde(default)]
    pub version: u32,

    /// Everything the sidecar described, or the reason it described nothing.
    pub state: BackupState,
}

impl BackupSummary {
    /// Builds the summary a completed creation returns and writes into the
    /// sidecar, at the current [`SUMMARY_VERSION`].
    #[must_use]
    pub fn readable(
        backup_id: String,
        manifest: BackupManifest,
        artifact_bytes: u64,
        artifact_sha256: String,
    ) -> Self {
        Self {
            backup_id,
            version: SUMMARY_VERSION,
            state: BackupState::Readable(ReadableBackup {
                manifest,
                artifact_bytes,
                artifact_sha256,
            }),
        }
    }

    /// Builds the summary a listing reports for an artifact this agent could
    /// not describe, for the given `reason`.
    ///
    /// One constructor and not one per reason, because the version this
    /// records is derived from the reason rather than passed alongside it:
    /// [`UnreadableReason::UnknownVersion`] is the only state that knows a
    /// version at all, and a second parameter would be a second chance for
    /// the two to disagree.
    #[must_use]
    pub fn unreadable(backup_id: String, reason: UnreadableReason) -> Self {
        let version = match &reason {
            UnreadableReason::UnknownVersion { version } => *version,
            UnreadableReason::Corrupt | UnreadableReason::NotARegularFile => 0,
        };

        Self {
            backup_id,
            version,
            state: BackupState::Unreadable(reason),
        }
    }

    /// Whether the sidecar beside this artifact could be read, parsed, and
    /// understood at [`SUMMARY_VERSION`].
    #[must_use]
    pub fn is_readable(&self) -> bool {
        matches!(self.state, BackupState::Readable(_))
    }

    /// What the sidecar described, or `None` when it could not be trusted.
    #[must_use]
    pub fn readable_details(&self) -> Option<&ReadableBackup> {
        match &self.state {
            BackupState::Readable(details) => Some(details),
            BackupState::Unreadable(_) => None,
        }
    }

    /// Why this entry is not readable, or `None` when it is.
    #[must_use]
    pub fn reason(&self) -> Option<&UnreadableReason> {
        match &self.state {
            BackupState::Readable(_) => None,
            BackupState::Unreadable(reason) => Some(reason),
        }
    }
}

#[cfg(test)]
#[path = "../../tests/backup/backup_summary_tests.rs"]
mod tests;
