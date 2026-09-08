//! The document that says what a backup contains.

use serde::{Deserialize, Serialize};

use crate::backup::model::manifest_database::ManifestDatabase;

/// The manifest version this agent writes, and the only one it will read.
///
/// An unknown version is REFUSED and never guessed: a restore that reads a
/// manifest it does not understand and proceeds on the fields it recognises is
/// a restore that silently skips whatever the newer version added.
pub const MANIFEST_VERSION: u32 = 1;

/// `manifest.json`, at the root of every artifact this area writes.
///
/// It exists so that a restore can answer three questions before it touches a
/// live account: is this archive for THIS account, does this agent understand
/// its layout, and is each dump inside it the dump this backup took. All three
/// have to be answerable from inside the archive, because the copy outside it
/// (the sidecar) is a separate file that can be edited without touching the
/// artifact — the restore reads both and refuses when they disagree.
///
/// Every field is a plain scalar or a list of them. Nothing here is a path: an
/// archive that carried the absolute paths it was made from would be a map of
/// the host for whoever downloads it, and a restore has no use for them — it
/// places members by the archive's fixed internal layout, not by what the
/// manifest says.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct BackupManifest {
    /// The layout version. [`MANIFEST_VERSION`] when this agent wrote it.
    pub version: u32,

    /// The account whose home and databases this archive holds.
    pub account: String,

    /// The backup's id, which is also the artifact's file name stem.
    pub backup_id: String,

    /// When the creation started, in seconds since the Unix epoch.
    pub created_at_unix: i64,

    /// The measured size of the account's home at that moment, in bytes,
    /// counting no member `tar` would not have archived.
    pub home_bytes: u64,

    /// One entry per dumped database, in the order they were dumped.
    pub databases: Vec<ManifestDatabase>,

    /// The version of the agent that wrote this archive.
    ///
    /// Recorded for the operator reading a five-month-old artifact, and never
    /// consulted by a restore: what a restore is allowed to depend on is
    /// [`Self::version`], which is the layout's own number and moves only when
    /// the layout does.
    pub agent_version: String,
}
