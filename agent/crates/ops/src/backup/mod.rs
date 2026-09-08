//! Backups: one gzip-compressed archive of an account's home and of one SQL
//! dump per database it owns, plus a manifest describing both.
//!
//! Three things shape everything in this area.
//!
//! **A backup is an ordinary `.tar.gz`, on purpose.** Not a content-addressed
//! repository (Decision 1). The most valuable property of a backup is that it
//! works when the panel does not: `tar xzf` runs from a rescue shell on a
//! machine with no panel, no network and no password, while a deduplicating
//! store needs its binary, its index and a key whose loss makes every backup
//! indistinguishable from noise. What that costs is stated rather than hidden —
//! every backup is a full backup, and seven dailies of a twenty-gigabyte
//! account are about a hundred and forty gigabytes before compression.
//!
//! **The archive's internal layout is fixed and is part of the contract:**
//! `manifest.json` at the root, `home/…` holding the account's home, and
//! `databases/<database>.sql` holding one dump each. Nothing else. A restore
//! refuses a member outside those three prefixes rather than skipping it — a
//! member it cannot place is an archive it does not understand.
//!
//! **A backup that half-succeeded is not a backup.** It is worse than no
//! backup, because a later restore would trust it. So the artifact's published
//! name is created by one `rename`, at the very end, after every dump has been
//! taken and hashed, the manifest written, the archiver exited zero and the
//! archive hashed; and every failure before that deletes the scratch and the
//! `.partial` and publishes nothing. [`create_backup`] carries the full
//! argument.
//!
//! **Only a directory on this machine is a destination today, and the object
//! store seam is landed but unreached.** This paragraph used to say that a
//! destination was "either a directory on this machine or an S3-compatible
//! bucket, and both are reached through [`ObjectStoreHost`], so `create`,
//! `restore`, `list` and `delete` have one code path". That was the design
//! intent and it is not the code. What is true, and was checked by grepping for
//! the callers rather than by reading the sentence above it:
//!
//! - [`create_backup`], [`restore_backup`], [`list_backups`] and
//!   [`delete_backup`] each take a
//!   [`LocalBackupRoot`](maran_agent_core::validation::system::local_backup_root::LocalBackupRoot)
//!   and open, rename and unlink files themselves. There is no store parameter
//!   on any of them.
//! - [`ObjectStoreHost`], [`LocalObjectStoreHost`], [`S3ObjectStoreHost`],
//!   [`object_key`], [`ObjectSummary`] and [`PublicReadVerdict`] have **no
//!   consumer anywhere in the workspace** outside this module's own
//!   re-exports and their own tests.
//! - **An S3 destination therefore cannot be created, restored, listed or
//!   deleted.** Nothing in this area's public surface can even name one: the
//!   only destination type the four operations accept is a local root, so the
//!   refusal is structural rather than a check that could be forgotten.
//!
//! Why it is landed this way and what it is waiting on is written on
//! [`ObjectStoreHost`] itself, which is where a reader of the seam arrives.
//! It is said here as well because this is the paragraph that misled: a reader
//! of [`create_backup`] who has been told the local case is one implementation
//! of a seam has no reason to re-derive that the remote half is missing, and
//! that mechanism — a comment that stops the next reader looking — is the one
//! rules/architecture.md names.
//!
//! What the remote implementation does get right, and what will still be true
//! when it is reached: it refuses a non-HTTPS endpoint at construction,
//! hand-writes its `Debug` so a credential cannot reach a log line through a
//! derive, and runs every provider message through a redactor before it crosses
//! back. Its public-read probe answers [`PublicReadVerdict`], which has three
//! states because "the anonymous GET could not be made" is not "the object is
//! private" and must never be reported as one.
//!
//! The area's shape is the one every area here has: one injectable host trait
//! ([`BackupHost`]), one implementation that really touches the machine
//! ([`ProcessBackupHost`]), one error enum ([`BackupError`]) that structurally
//! cannot carry a tool's output or a customer's rows, and `model/` for the
//! documents and the typed inputs and outputs. It has a second seam,
//! [`DatabaseCatalog`], because the databases an account owns are another
//! area's knowledge and `ops::backup` does not import another `ops` area — the
//! service layer composes the two.

pub mod archive;
mod backup_error;
mod backup_host;
// Public: the question of whether `tar`, `gzip` and the database dump client
// are actually present and executable on THIS host, asked at agent startup so
// a dropped package is loud before a scheduled backup finds it at 03:00
// instead of after. `executable_lookup`/`real_executable_lookup` are the
// injectable filesystem seam it is checked through.
mod executable_lookup;
// Private: the per-account lock. Nothing outside this area starts an operation,
// so nothing outside it has a reason to take one.
mod backup_lock;
// Private: the inode check that stands between a validated configuration string
// and a directory somebody chmodded since. Its only callers are this area's
// operations, which is the point — it is not an inspection a caller may skip.
mod backup_root;
mod create_backup;
mod database_catalog;
mod delete_backup;
mod list_backups;
mod local_object_store_host;
pub mod model;
mod object_key;
mod object_store_host;
mod process_backup_host;
// Private: the one place a sidecar's bytes become a trusted `BackupSummary`.
// `list_backups` and `restore_backup` both call it rather than each parsing
// the file and checking its version on its own.
mod read_sidecar;
mod real_executable_lookup;
#[cfg(test)]
#[path = "../tests/backup/recording_backup_host.rs"]
pub(crate) mod recording_backup_host;
// Private: the pre-flight refusal. The scratch filesystem is asked whether it
// can take what is about to be written, before it is written.
mod require_scratch_room;
mod restore_backup;
mod root_only_chain;
mod s3_object_store_host;
// Private: the live per-dump ceiling, derived from the same measurement.
mod scratch_dump_ceiling;
mod verify_backup_binaries;

pub use backup_error::BackupError;
pub use backup_host::BackupHost;
pub use create_backup::create_backup;
pub use database_catalog::DatabaseCatalog;
pub use delete_backup::delete_backup;
pub use executable_lookup::ExecutableLookup;
pub use list_backups::list_backups;
pub use local_object_store_host::LocalObjectStoreHost;
pub use model::archive_part::ArchivePart;
pub use model::backup_manifest::{BackupManifest, MANIFEST_VERSION};
pub use model::backup_stage::BackupStage;
pub use model::backup_state::BackupState;
pub use model::backup_summary::BackupSummary;
pub use model::extract_identity::ExtractIdentity;
pub use model::extract_spec::ExtractSpec;
pub use model::manifest_database::ManifestDatabase;
pub use model::object_summary::ObjectSummary;
pub use model::progress_sink::ProgressSink;
pub use model::public_read_verdict::PublicReadVerdict;
pub use model::readable_backup::ReadableBackup;
pub use model::restore_outcome::RestoreOutcome;
pub use model::restore_sink::RestoreSink;
pub use model::restore_stage::RestoreStage;
pub use model::unreadable_reason::UnreadableReason;
pub use object_key::object_key;
pub use object_store_host::ObjectStoreHost;
pub use process_backup_host::ProcessBackupHost;
pub use real_executable_lookup::RealExecutableLookup;
pub use restore_backup::restore_backup;
pub use s3_object_store_host::S3ObjectStoreHost;
pub use verify_backup_binaries::verify_backup_binaries;
