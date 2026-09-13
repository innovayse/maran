//! Turning one listed backup into the message the panel reads.

use maran_ops::backup::{
    BackupManifest, BackupState, BackupSummary, ManifestDatabase, ReadableBackup, UnreadableReason,
};

use crate::proto::{
    BackupInfo, BackupManifest as WireManifest, ManifestDatabase as WireDatabase,
    ReadableBackup as WireReadable, UnreadableKind, UnreadableReason as WireUnreadable,
    backup_info,
};

/// Converts a listed backup into its wire message.
///
/// # The oneof is set from a Rust enum, so the arm cannot disagree with itself
///
/// [`BackupState`] is `Readable(ReadableBackup) | Unreadable(UnreadableReason)`
/// and the readable arm holds its manifest, its size and its digest as required
/// fields — there is no value of it that claims to be readable and carries no
/// digest. This function maps that structure onto the wire's `oneof`, so the
/// message this agent emits has a manifest in every readable arm as a
/// consequence of the type it was built from, not of a check somebody
/// remembered.
///
/// **The wire does not carry that guarantee, and this is the honest limit.**
/// proto3's `oneof` makes the arm exactly-one; it does not make
/// `ReadableBackup.manifest` present, so a peer other than this agent can send
/// a readable arm with no manifest. The refusal of that message belongs to
/// whoever READS a `BackupInfo` — the C# client — and it is not written here,
/// because this agent only produces them: a conversion in this direction that
/// refused an absent manifest would be a branch no call site can reach, which
/// is the same unreachable-refusal defect the object-store seam was found to
/// have. What is written here instead is the property a reader can rely on and
/// a test can observe: this agent never emits one.
///
/// # The three flat fields
///
/// `size_bytes`, `created_at_unix` and `sha256` predate the `oneof` and cannot
/// be removed inside v1 (rules/proto.md). They are filled from the readable arm
/// and left at their defaults for an unreadable entry — the sidecar is the only
/// source for all three, and an unreadable entry has no trustworthy one. A
/// reader that switches on `state` never has to decide what a zero means.
#[must_use]
pub fn to_backup_info(summary: BackupSummary) -> BackupInfo {
    let BackupSummary {
        backup_id, state, ..
    } = summary;

    match state {
        BackupState::Readable(details) => BackupInfo {
            backup_id,
            size_bytes: details.artifact_bytes,
            created_at_unix: details.manifest.created_at_unix,
            sha256: details.artifact_sha256.clone(),
            state: Some(backup_info::State::Readable(to_readable(details))),
        },
        BackupState::Unreadable(reason) => BackupInfo {
            backup_id,
            size_bytes: 0,
            created_at_unix: 0,
            sha256: String::new(),
            state: Some(backup_info::State::Unreadable(to_unreadable(&reason))),
        },
    }
}

/// Maps the readable arm, manifest included.
///
/// `manifest` is `Some` unconditionally, which is what makes the emitted
/// message honour the invariant [`ReadableBackup`] holds in Rust.
fn to_readable(details: ReadableBackup) -> WireReadable {
    WireReadable {
        manifest: Some(to_manifest(details.manifest)),
        artifact_bytes: details.artifact_bytes,
        artifact_sha256: details.artifact_sha256,
    }
}

/// Maps the manifest document, database lines included.
fn to_manifest(manifest: BackupManifest) -> WireManifest {
    WireManifest {
        version: manifest.version,
        account: manifest.account,
        backup_id: manifest.backup_id,
        created_at_unix: manifest.created_at_unix,
        home_bytes: manifest.home_bytes,
        databases: manifest.databases.into_iter().map(to_database).collect(),
        agent_version: manifest.agent_version,
    }
}

/// Maps one database's line in the manifest.
fn to_database(database: ManifestDatabase) -> WireDatabase {
    WireDatabase {
        name: database.name,
        bytes: database.bytes,
        sha256: database.sha256,
    }
}

/// Maps the reason an entry could not be described.
///
/// The version travels only with [`UnreadableReason::UnknownVersion`] and is
/// zero for the other two, which mirrors the domain type: it is the one state
/// that knows a version, and a second field filled for the others would be a
/// number a reader could believe.
fn to_unreadable(reason: &UnreadableReason) -> WireUnreadable {
    let (kind, version) = match reason {
        UnreadableReason::Corrupt => (UnreadableKind::Corrupt, 0),
        UnreadableReason::UnknownVersion { version } => (UnreadableKind::UnknownVersion, *version),
        UnreadableReason::NotARegularFile => (UnreadableKind::NotARegularFile, 0),
    };

    WireUnreadable {
        kind: kind as i32,
        version,
    }
}

#[cfg(test)]
#[path = "../../tests/services/backup/to_backup_info_tests.rs"]
mod tests;
