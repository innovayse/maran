//! The seam between a backup operation and the place the bytes rest.

use std::path::Path;

use crate::backup::backup_error::BackupError;
use crate::backup::model::object_summary::ObjectSummary;
use crate::backup::model::progress_sink::ProgressSink;
use crate::backup::model::public_read_verdict::PublicReadVerdict;

/// Everything this area asks of the place a backup is stored.
///
/// # NOTHING CALLS THIS TRAIT YET
///
/// Stated first because it is the fact a reader most needs and the one this doc
/// comment used to deny. `grep -rn "ObjectStoreHost" agent/crates/` answers this
/// file, its two implementations, three `mod.rs` lines and the tests, and
/// nothing else. The four operations —
/// [`create_backup`](crate::backup::create_backup),
/// [`restore_backup`](crate::backup::restore_backup),
/// [`list_backups`](crate::backup::list_backups) and
/// [`delete_backup`](crate::backup::delete_backup) — take a
/// [`LocalBackupRoot`](maran_agent_core::validation::system::local_backup_root::LocalBackupRoot)
/// and touch the filesystem directly. **There is no code above this trait.**
///
/// The sentence that stood here — "Above this trait there is one code path" —
/// was written as the design's intent and read as a statement about the code.
/// It is replaced rather than softened, because a doc comment describing what
/// the code does not do is a defect of the same severity as the behaviour
/// (rules/architecture.md), and this one was load-bearing: it told the reader
/// of a local operation that the remote half existed somewhere above.
///
/// # Why the seam is right, and what it is actually waiting on
///
/// The argument below is unchanged and is the reason this trait is kept rather
/// than deleted. A backup's destination is either a directory on this machine or
/// a bucket somewhere else, and those are genuinely different things — one is a
/// `rename`, the other is a signed multipart upload over TLS. The tempting shape
/// is a `DestinationKind` enum that each operation matches on, and it is the
/// wrong one: `create`, `restore`, `list` and `delete` would each grow the same
/// two-armed branch, the arms would drift, and every later feature (retention,
/// the destination probe, the pre-restore backup) would add its own copy. Four
/// operations times two arms is eight places for the local case to be subtly
/// different from the remote one, and nothing would be watching.
///
/// What stops the operations being re-signatured onto it today is three
/// measured obstacles, not an unfinished afternoon's work. They are written
/// here so that the next person to attempt the wiring starts from them instead
/// of rediscovering them:
///
/// 1. **A listing cannot be expressed in keys.**
///    [`list_backups`](crate::backup::list_backups) reports a name that is not
///    valid UTF-8 as [`BackupError::UnmintedArtifactName`] and a directory or
///    symbolic link wearing an artifact's name as
///    [`UnreadableReason::NotARegularFile`](crate::backup::UnreadableReason::NotARegularFile).
///    Both are facts about an inode; [`Self::list`] answers
///    [`ObjectSummary`], which is a key and a byte count, and a bucket has
///    neither directories nor links. Routing the listing through this seam would
///    not move those two refusals — it would delete them. A listing also needs
///    the sidecar's CONTENTS, and this trait offers no way to read an object
///    except [`Self::get`] into a file.
/// 2. **A creation and a restore would each gain a full copy of the artifact.**
///    `create_backup` has `tar` write the artifact into the destination
///    directory and publishes it with one `rename`; a restore reads it in place,
///    in five `tar` passes over a path. Through this seam the creation must
///    build somewhere else and [`Self::put`] it, and the restore must
///    [`Self::get`] it before the first pass — which for a LOCAL destination is
///    a byte-for-byte copy of an artifact bounded only by the shipped
///    `MAXIMUM_ARCHIVE_BYTES`, sixty-four gibibytes. That is a disk-headroom
///    decision for every existing local install, not a refactor.
/// 3. **The remote arm needs artifact-sized local staging.** `tar` takes a path,
///    so a restore from a bucket downloads the whole artifact first.
///    [`AgentPaths::BULK_SCRATCH_ROOT`](maran_agent_core::agent_paths::AgentPaths::BULK_SCRATCH_ROOT)
///    is where that would go and it is disk-backed, which is the precondition.
///    What is missing is the ceiling: no operation in this crate consults the
///    free space under that root before staging, so a download would be bounded
///    only by `MAXIMUM_ARCHIVE_BYTES`. The primitive to ask with exists
///    (`maran_agent_core::utils::available_bytes`) and this crate does call it —
///    on the dump paths, through `scratch_dump_ceiling` and
///    `require_scratch_room` — but no caller consults it for an artifact-sized
///    download under that root.
///
/// So the local destination IS a shortcut through the area today, and this
/// trait's [`LocalObjectStoreHost`](crate::backup::LocalObjectStoreHost) is a
/// second implementation of publishing that nothing calls. Whether that is
/// resolved by wiring the seam (paying obstacle 2) or by withdrawing it is an
/// owner's decision, and the code says which is true meanwhile rather than
/// which was hoped for.
///
/// [`BackupError::UnmintedArtifactName`]: crate::backup::BackupError::UnmintedArtifactName
/// [`ObjectSummary`]: crate::backup::ObjectSummary
///
/// # A key, not a path
///
/// Every method addresses an object by KEY — the `/`-separated string
/// [`object_key`](crate::backup::object_key) builds — and never by a filesystem
/// path. A bucket has no filesystem, and the local implementation is the one
/// that turns a key into a path (under its own root, and only under its own
/// root). A seam that spoke in paths would make the S3 implementation invent a
/// mapping back, and would let a caller hand the local implementation an
/// absolute path to somewhere else entirely.
///
/// # The blocking obligation
///
/// **Every method of this trait MUST be called from
/// `tokio::task::spawn_blocking`, never from a runtime worker.** Each of them
/// waits on real I/O for as long as a customer's backup takes — tens of minutes
/// for a large account — and on a runtime worker that stalls every other
/// in-flight command (rules/rust.md "Async and blocking"). The S3
/// implementation additionally bridges an async client on a
/// `tokio::runtime::Handle`, which is only legal off a worker thread. The
/// obligation is restated on each method, because a caller reads the method
/// they are calling.
pub trait ObjectStoreHost: Send + Sync {
    /// Uploads the file at `from` to `key` and answers how many bytes it sent.
    ///
    /// The object is written so that only the destination's owner can read it:
    /// mode `0600` on the local implementation, and the bucket's default
    /// (private) ACL on the remote one. An implementation MUST NOT make an
    /// object more readable than that.
    ///
    /// Progress is reported under [`BackupStage::Uploading`], interpolated from
    /// bytes sent against the file's size. No implementation reports a number
    /// it did not compute (rules/testing.md; the account-deletion cascade's
    /// literal 10/50/90).
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// [`BackupStage::Uploading`]: crate::backup::BackupStage::Uploading
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] when the destination refused or could
    /// not be reached, and [`BackupError::ChecksumUnreadable`] when the local
    /// file could not be read to the end — an upload that sent part of a file
    /// is not a shorter backup.
    fn put(&self, key: &str, from: &Path, sink: &mut dyn ProgressSink) -> Result<u64, BackupError>;

    /// Downloads `key` into the file at `into` and answers how many bytes it
    /// received.
    ///
    /// `into` is created by this call, mode `0600`, and is left in place only
    /// if the whole object arrived: a truncated download is an artifact a
    /// restore would checksum and reject, and leaving it behind under the name
    /// the caller chose is how a half-written file gets read by the next thing
    /// along.
    ///
    /// **No progress is reported, and this method has no caller at all.** The
    /// second half of that sentence is the correction: this doc used to say
    /// "the only caller of this method is a restore, whose stages are its own
    /// and do not exist yet", and both halves were wrong —
    /// [`RestoreStage`](crate::backup::RestoreStage) exists in
    /// `model/restore_stage.rs`, and `restore_backup` reads its artifact from a
    /// path without going through this trait. See the trait's own doc for why
    /// nothing above it exists.
    ///
    /// The reason there is no sink parameter stands on its own and is unchanged
    /// by that. The one sink this area's creation has speaks in
    /// [`BackupStage`](crate::backup::BackupStage), whose spans are a
    /// CREATION's — `uploading` owns 80→99. Reporting a download as
    /// `uploading`, or interpolating a percentage inside a span that belongs to
    /// a different operation, would be a number that means nothing, which is the
    /// defect this area's progress reporting is shaped against. The parameter is
    /// therefore absent rather than accepted and ignored; it lands, taking a
    /// [`RestoreSink`](crate::backup::RestoreSink), with the caller that can
    /// give it a truthful argument.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] when the object could not be fetched,
    /// [`BackupError::ObjectNotFound`] when the destination does not hold it,
    /// and [`BackupError::ArtifactUnpublishable`] when the local file could not
    /// be created or written.
    fn get(&self, key: &str, into: &Path) -> Result<u64, BackupError>;

    /// Removes `key`, and reports success when it was already absent.
    ///
    /// Idempotent by rule: a delete whose response was lost is retried, and the
    /// retry must not turn a completed removal into a failure.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] when the destination refused for any
    /// reason other than the object not being there.
    fn delete(&self, key: &str) -> Result<(), BackupError>;

    /// Answers every object whose key starts with `prefix`.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] when the destination could not be
    /// listed. **A destination that cannot be listed is an error and never an
    /// empty list**: retention prunes the oldest of what a listing returns, so
    /// an empty answer from a failed listing is indistinguishable from an
    /// account with no backups, and the difference decides whether anything is
    /// deleted.
    fn list(&self, prefix: &str) -> Result<Vec<ObjectSummary>, BackupError>;

    /// Answers what an anonymous reader gets when it asks this destination for
    /// `key`.
    ///
    /// The caller writes the object at `key` first and removes it afterwards,
    /// through [`Self::put`] and [`Self::delete`] on this same host — which is
    /// why this method takes a key and not a destination. The object it probes
    /// must therefore be a canary the panel wrote for the purpose, never a
    /// customer's artifact: a probe of a real backup that came back
    /// [`PublicReadVerdict::PubliclyReadable`] would have proved the exposure
    /// by performing it.
    ///
    /// The fetch carries **no credentials of any kind** — that is the whole
    /// content of the check, and an implementation that signs the request is
    /// asking a different question and will answer `Private` about a bucket the
    /// internet can read.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] only for a failure of this process —
    /// a runtime that could not be entered. **A failure of the FETCH is not an
    /// error; it is [`PublicReadVerdict::Unproven`]**, and the distinction is
    /// the point of the type.
    fn probe_public_read(&self, key: &str) -> Result<PublicReadVerdict, BackupError>;
}
