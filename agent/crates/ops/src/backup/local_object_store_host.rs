//! The [`ObjectStoreHost`] a destination on this machine is reached through.

use std::fs::{DirBuilder, File, read_dir, remove_file, rename};
use std::io::{Read as _, Write as _};
use std::os::unix::fs::{DirBuilderExt as _, OpenOptionsExt as _};
use std::path::{Path, PathBuf};

use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;

use crate::backup::backup_error::BackupError;
use crate::backup::model::backup_stage::BackupStage;
use crate::backup::model::object_summary::ObjectSummary;
use crate::backup::model::progress_sink::ProgressSink;
use crate::backup::model::public_read_verdict::PublicReadVerdict;
use crate::backup::object_store_host::ObjectStoreHost;

/// The mode every object this host writes carries: root's alone.
///
/// The same `0600` `create_backup` publishes a local artifact with, and for the
/// same reason — the object holds every file in a customer's home and a full
/// dump of every database they own (rules/security.md item 8).
const OBJECT_MODE: u32 = 0o600;

/// The mode every directory this host creates carries.
const DIRECTORY_MODE: u32 = 0o700;

/// How much is copied per read/write turn, and therefore how often progress
/// moves.
///
/// Eight mebibytes: large enough that the syscall overhead is nothing against a
/// multi-gigabyte artifact, small enough that a customer's archive is never in
/// memory whole (rules/rust.md "Streams stay bounded" — an artifact may be
/// sixty-four gibibytes).
const COPY_CHUNK_BYTES: usize = 8 * 1024 * 1024;

/// The suffix an object wears while it is being written into this destination.
const PARTIAL_SUFFIX: &str = ".partial";

/// A backup destination that is a directory on this machine.
///
/// # Nothing constructs this type outside its own tests
///
/// The four operations write to the local destination themselves, and none of
/// them holds an [`ObjectStoreHost`]; the trait's own doc carries the evidence
/// and the three obstacles to changing that. So this type is a SECOND
/// implementation of "publish a backup into a directory on this machine",
/// beside the one `create_backup` actually runs, and the two are not the same
/// code: `put` streams a copy between two files in eight-mebibyte chunks, while
/// `create_backup::publish` renames a file the archiver already wrote and first
/// re-`stat`s it for uid, exact mode and `nlink == 1`. Neither can be replaced
/// by a call to the other — one would lose the inode check, the other would gain
/// a copy of up to sixty-four gibibytes — so this is not the rule of two firing;
/// it is two publications of one thing existing because only one of them is
/// reached.
///
/// # Why the local destination would be an implementation and not a branch
///
/// The design argument, kept because it is the reason this type is here rather
/// than deleted. It would be shorter to let `create`, `restore`, `list` and
/// `delete` notice that the destination is local and write the file themselves
/// — which, today, is exactly what they do. It would also mean that "a backup is
/// published by writing it under a `.partial` name and renaming it into place"
/// is a decision made in four operations rather than one, and that the local
/// half of each of them is a code path the remote half never exercises. What
/// this type is meant to add is the demonstration that the local case really
/// does fit through the seam — every method below is the same contract the S3
/// implementation keeps, expressed in `rename` and `read_dir`. That
/// demonstration is currently made only by this file's own tests.
///
/// # What a key is, here
///
/// A key is a relative path under this host's own root, and this type is the only
/// thing that turns one into a filesystem path. Keys are built by
/// [`object_key`](crate::backup::object_key) from validated values and cannot
/// contain `..` or a leading separator; `path_of` refuses one that does
/// anyway, because the seam is a `&str` and the day somebody hands it a
/// composed string is the day a backup is written over `/etc`.
///
/// # `Debug`
///
/// Derived, and safe to derive: this host holds a directory path and nothing
/// else. There is no credential here to omit — which is exactly why the S3
/// implementation's `Debug` is hand-written and this one is not.
#[derive(Debug)]
pub struct LocalObjectStoreHost {
    /// The canonicalised directory every key is resolved under.
    root: PathBuf,
}

impl LocalObjectStoreHost {
    /// Opens the destination rooted at `root`.
    ///
    /// The root is resolved through [`LocalBackupRoot::resolve`] here, once, at
    /// construction: every later key is joined onto the resolved answer, so a
    /// symlink swapped in afterwards moves nothing this host writes. What it
    /// does NOT do is re-check the root's ownership and mode — that is
    /// `backup_root`'s job, it is asked immediately before each creation
    /// because a mode can change between two writes, and duplicating it here
    /// would be a second answer to a question that already has an owner.
    ///
    /// # Errors
    ///
    /// [`BackupError::BackupRootUnusable`] when the configured root does not
    /// resolve — reported as unusable rather than assumed present, because a
    /// destination this host cannot resolve is one it must not silently create
    /// somewhere else.
    pub fn new(root: &LocalBackupRoot) -> Result<Self, BackupError> {
        let resolved = root
            .resolve()
            .map_err(|_| BackupError::BackupRootUnusable)?;

        Ok(Self { root: resolved })
    }

    /// Resolves `key` to the path it names under [`Self::root`].
    ///
    /// Refuses a key that is absolute, that carries a `..` or `.` segment, that
    /// has an empty segment, or that is empty. Every one of those is impossible
    /// for a key [`object_key`](crate::backup::object_key) built — and this
    /// function exists for the keys it did not build. A relative-path check
    /// written once, at the only place a key becomes a path, is cheaper than
    /// trusting four callers.
    ///
    /// # Errors
    ///
    /// [`BackupError::ObjectStoreFailed`] naming the refusal. The message
    /// carries the reason and never the key: a key is derived from an account
    /// name and a backup id, and neither belongs in an error that reaches a
    /// log line.
    fn path_of(&self, key: &str) -> Result<PathBuf, BackupError> {
        let usable = !key.is_empty()
            && !key.starts_with('/')
            && key
                .split('/')
                .all(|segment| !segment.is_empty() && segment != "." && segment != "..");
        if !usable {
            return Err(BackupError::ObjectStoreFailed {
                message: "the object key is not a relative path under the destination".to_owned(),
            });
        }

        Ok(self.root.join(key))
    }
}

impl ObjectStoreHost for LocalObjectStoreHost {
    /// Copies `from` to the key's path, root-only, and publishes it by rename.
    ///
    /// The `.partial`-then-`rename` shape is the same one `create_backup` uses
    /// for the artifact it builds, and it is here for the same reason: a reader
    /// of this destination — a listing, a restore, retention — must never see a
    /// name that exists but is half-written.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::put`].
    fn put(&self, key: &str, from: &Path, sink: &mut dyn ProgressSink) -> Result<u64, BackupError> {
        let destination = self.path_of(key)?;
        if let Some(parent) = destination.parent() {
            create_dir_all_root_only(parent)?;
        }

        let mut source = File::open(from).map_err(|_| BackupError::ChecksumUnreadable)?;
        let total = source
            .metadata()
            .map_err(|_| BackupError::ChecksumUnreadable)?
            .len();

        let mut partial = destination.clone().into_os_string();
        partial.push(PARTIAL_SUFFIX);
        let partial = PathBuf::from(partial);
        let _ = remove_file(&partial);

        let copied = copy_into(&mut source, &partial, total, sink);
        match copied {
            Ok(copied) => {
                rename(&partial, &destination).map_err(|_| BackupError::ObjectStoreFailed {
                    message: "the object could not be published in the destination".to_owned(),
                })?;
                Ok(copied)
            }
            Err(error) => {
                let _ = remove_file(&partial);
                Err(error)
            }
        }
    }

    /// Copies the key's path to `into`, root-only.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::get`].
    fn get(&self, key: &str, into: &Path) -> Result<u64, BackupError> {
        let source = self.path_of(key)?;
        let mut source = File::open(&source).map_err(|_| BackupError::ObjectNotFound)?;
        let total = source
            .metadata()
            .map_err(|_| BackupError::ObjectStoreFailed {
                message: "the object could not be measured".to_owned(),
            })?
            .len();

        let mut nowhere = NoProgress;
        match copy_into(&mut source, into, total, &mut nowhere) {
            Ok(copied) => Ok(copied),
            Err(error) => {
                // A truncated download is not a shorter artifact, and leaving it
                // under the name the caller chose is how a half-written file
                // gets read by the next thing along.
                let _ = remove_file(into);
                Err(error)
            }
        }
    }

    /// Removes the key's path, and reports success when it was already gone.
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::delete`].
    fn delete(&self, key: &str) -> Result<(), BackupError> {
        let path = self.path_of(key)?;
        match remove_file(&path) {
            Ok(()) => Ok(()),
            Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(()),
            Err(_) => Err(BackupError::ObjectStoreFailed {
                message: "the object could not be removed from the destination".to_owned(),
            }),
        }
    }

    /// Answers every regular file directly under the prefix's directory.
    ///
    /// Directly under, and not a recursive walk: this destination's keys are
    /// `<prefix>/<account>/<id>.tar.gz`, so one account's objects are one
    /// directory's entries, and a recursive walk would let a directory somebody
    /// created under the backup root turn a listing into a filesystem crawl.
    ///
    /// A prefix whose directory does not exist answers an empty list, because
    /// an account that has never been backed up has no directory and no
    /// backups, and those are the same fact. A directory that exists and cannot
    /// be READ is an error: retention prunes the oldest of what a listing
    /// returns, and an empty answer from a failed read would be read as "this
    /// account has no backups".
    ///
    /// # Errors
    ///
    /// As documented on [`ObjectStoreHost::list`].
    fn list(&self, prefix: &str) -> Result<Vec<ObjectSummary>, BackupError> {
        let directory = self.path_of(prefix.trim_end_matches('/'))?;
        if !directory.is_dir() {
            return Ok(Vec::new());
        }

        let entries = read_dir(&directory).map_err(|_| BackupError::ObjectStoreFailed {
            message: "the destination could not be listed".to_owned(),
        })?;

        let mut summaries = Vec::new();
        for entry in entries {
            let entry = entry.map_err(|_| BackupError::ObjectStoreFailed {
                message: "the destination could not be listed".to_owned(),
            })?;
            let metadata = entry
                .metadata()
                .map_err(|_| BackupError::ObjectStoreFailed {
                    message: "an object in the destination could not be measured".to_owned(),
                })?;
            if !metadata.is_file() {
                continue;
            }
            let Some(name) = entry.file_name().to_str().map(str::to_owned) else {
                continue;
            };
            summaries.push(ObjectSummary {
                key: format!("{}{name}", with_trailing_separator(prefix)),
                bytes: metadata.len(),
            });
        }

        summaries.sort_by(|left, right| left.key.cmp(&right.key));
        Ok(summaries)
    }

    /// Reports [`PublicReadVerdict::Unproven`], always, and says why.
    ///
    /// **This is the honest answer and not a stub.** The question the verdict
    /// answers is what an anonymous HTTP client gets, and there is no anonymous
    /// HTTP client for a directory on this machine — nothing here can be
    /// fetched, so nothing here can be observed being fetched. Returning
    /// [`PublicReadVerdict::Private`] would be a green answer produced by a
    /// check that made no observation, which is the defect this whole type
    /// exists against (rules/testing.md, "a check must be able to observe what
    /// it reports on").
    ///
    /// What actually keeps a local destination off the internet is stated where
    /// it is enforced and is deliberately NOT re-derived here: the configured
    /// root is refused by `LocalBackupRoot::parse` if it is under `/home`, under
    /// a site's document root, or under a world-writable directory, and
    /// `backup_root` asks the inode, immediately before every write, whether
    /// the directory is still root's alone.
    ///
    /// Because [`PublicReadVerdict::destination_is_acceptable`] is false for
    /// this, a caller that probed a local destination refuses it. That is the
    /// safe direction and it is intentional: the panel probes S3 destinations
    /// (R9), and a caller reaching here has asked a question about a place that
    /// cannot answer it.
    ///
    /// # Errors
    ///
    /// None. The signature is the seam's.
    fn probe_public_read(&self, _key: &str) -> Result<PublicReadVerdict, BackupError> {
        Ok(PublicReadVerdict::Unproven {
            reason: "a local destination is not served over HTTP, so an anonymous fetch of it \
                     cannot be made; its exposure is decided by the backup root's validation \
                     and by the inode check before each write"
                .to_owned(),
        })
    }
}

/// A sink for a transfer this area has no truthful stage to report under.
///
/// A named type rather than an `Option<&mut dyn ProgressSink>` threaded through
/// [`copy_into`]: an option would put a branch on every chunk, and the reason
/// there is no reporting is a fact about the operation, not a runtime
/// condition. See [`ObjectStoreHost::get`] for the argument.
struct NoProgress;

impl ProgressSink for NoProgress {
    /// Discards the report.
    fn report(&mut self, _stage: BackupStage, _percent: u32) {}
}

/// Copies `total` bytes from `source` into a new root-only file at
/// `destination`, reporting progress as it goes.
///
/// The file is created with [`OBJECT_MODE`] by `create_new`, so it is made by
/// this call or not at all: an object written into a file that was already
/// there would inherit that file's mode and its other hard links.
///
/// `fsync` before returning. A backup that is in the page cache when the
/// machine loses power is a backup that does not exist, and this is the last
/// moment anything in this product can say so.
///
/// # Errors
///
/// [`BackupError::ChecksumUnreadable`] when the source could not be read to the
/// end, [`BackupError::ObjectStoreFailed`] when the destination could not be
/// created or written.
fn copy_into(
    source: &mut File,
    destination: &Path,
    total: u64,
    sink: &mut dyn ProgressSink,
) -> Result<u64, BackupError> {
    let mut target = File::options()
        .write(true)
        .create_new(true)
        .mode(OBJECT_MODE)
        .open(destination)
        .map_err(|_| BackupError::ObjectStoreFailed {
            message: "the object could not be created in the destination".to_owned(),
        })?;

    let mut buffer = vec![0_u8; COPY_CHUNK_BYTES];
    let mut copied: u64 = 0;
    loop {
        let read = source
            .read(&mut buffer)
            .map_err(|_| BackupError::ChecksumUnreadable)?;
        if read == 0 {
            break;
        }
        target
            .write_all(&buffer[..read])
            .map_err(|_| BackupError::ObjectStoreFailed {
                message: "the object could not be written to the destination".to_owned(),
            })?;
        copied = copied.saturating_add(read as u64);
        sink.report(
            BackupStage::Uploading,
            BackupStage::Uploading.percent_through(copied, total),
        );
    }

    target
        .sync_all()
        .map_err(|_| BackupError::ObjectStoreFailed {
            message: "the object could not be flushed to the destination".to_owned(),
        })?;

    Ok(copied)
}

/// Creates `directory` and its missing parents, root-only.
///
/// The mode is applied to the directories this call creates; one that already
/// exists is left as it is, because its mode is `backup_root`'s question and
/// answering it here would give it two owners.
///
/// # Errors
///
/// [`BackupError::ObjectStoreFailed`].
fn create_dir_all_root_only(directory: &Path) -> Result<(), BackupError> {
    if directory.is_dir() {
        return Ok(());
    }

    DirBuilder::new()
        .recursive(true)
        .mode(DIRECTORY_MODE)
        .create(directory)
        .map_err(|_| BackupError::ObjectStoreFailed {
            message: "the destination directory could not be created".to_owned(),
        })
}

/// `prefix` with exactly one trailing `/`, so a key built from it has exactly
/// one separator.
fn with_trailing_separator(prefix: &str) -> String {
    if prefix.is_empty() || prefix.ends_with('/') {
        prefix.to_owned()
    } else {
        format!("{prefix}/")
    }
}

#[cfg(test)]
#[path = "../tests/backup/local_object_store_host_tests.rs"]
mod tests;
