//! What a sidecar says when it could be read: the whole of it, or nothing.

use serde::{Deserialize, Serialize};

use crate::backup::model::backup_manifest::BackupManifest;

/// Everything a readable sidecar describes about one finished backup.
///
/// **Every field is required, and that is the whole point of this type
/// existing.** These three facts used to be three `Option` fields sitting
/// beside a `readable: bool` on
/// [`BackupSummary`](crate::backup::model::backup_summary::BackupSummary),
/// with the rule "`readable` implies all three are `Some`" written in a doc
/// comment and enforced only by the named constructors. A sidecar is read by
/// `serde`, not by those constructors, and `serde` treats an absent
/// `Option` field as `None` — so a document reading
/// `{"version":1,"readable":true}` deserialised into a summary that claimed
/// to be readable and carried no manifest and no digest, which is exactly the
/// state the doc promised could not exist. Making the three facts one struct
/// that either arrives whole or fails to parse moves the promise from prose
/// into the shape, where `serde` has to keep it too.
///
/// The digest is the field that makes this more than tidiness: a restore
/// compares the artifact's bytes against
/// [`Self::artifact_sha256`](Self::artifact_sha256), and a missing digest that
/// arrives labelled "readable" is a comparison that never happens against a
/// value nobody supplied.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct ReadableBackup {
    /// The manifest, exactly as it was written into the archive.
    pub manifest: BackupManifest,

    /// Size of the published `.tar.gz`, in bytes.
    pub artifact_bytes: u64,

    /// SHA-256 of the published `.tar.gz`, hex, lowercase.
    ///
    /// Taken over the finished file, after `tar` exited and before the
    /// publishing rename, so it describes the bytes a restore will read.
    pub artifact_sha256: String,
}
