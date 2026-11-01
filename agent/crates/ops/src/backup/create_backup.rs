//! CreateBackup: one archive of an account's home and databases.

use std::fs::{remove_dir_all, remove_file};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::archive::archive_home::archive_home;
use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::dump_database::dump_database;
use crate::backup::archive::measure_home::measure_home_bytes;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_root::prepare_account_directory;
use crate::backup::create_partial::create_partial;
use crate::backup::database_catalog::DatabaseCatalog;
use crate::backup::list_archive_members::{LISTING_REFUSED, MAXIMUM_MEMBERS};
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::backup_manifest::{BackupManifest, MANIFEST_VERSION};
use crate::backup::model::backup_stage::BackupStage;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::model::progress_sink::ProgressSink;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::open_backup_scratch::open_backup_scratch;
use crate::backup::publish_artifact::publish_artifact;
use crate::backup::scratch_dump_ceiling::scratch_dump_ceiling;
use crate::backup::write_sidecar::write_sidecar;

/// The largest dump this agent will archive, per database.
///
/// A cap, and no longer the whole ceiling. The effective ceiling for each dump
/// is the smaller of this number and nine tenths of what the scratch
/// filesystem has left at the moment that dump is taken
/// ([`scratch_dump_ceiling`]), because a constant cannot know what a host has.
/// This one exists so that an operator gets a readable refusal for a single
/// pathological database rather than a filesystem that filled at 03:00, on a
/// disk — which `AgentPaths::BULK_SCRATCH_ROOT` is, since the staging moved off
/// the tmpfs under `/run`.
const MAXIMUM_DUMP_BYTES: u64 = 8 * 1024 * 1024 * 1024;

/// The largest finished artifact this agent will publish.
const MAXIMUM_ARCHIVE_BYTES: u64 = 64 * 1024 * 1024 * 1024;

/// The suffix the artifact wears while it is being written.
///
/// The published name never exists until the whole backup has succeeded: `tar`
/// writes THIS name, and the final name comes into being by a rename. A reader
/// therefore never sees a half-written archive, and a run killed at any point
/// leaves a file whose extension says exactly what it is.
const PARTIAL_SUFFIX: &str = ".partial";

/// The uid the artifact must belong to on a real host — and the uid every
/// directory of the scratch chain must belong to, for the same reason.
const ROOT_UID: u32 = 0;

/// Where one creation puts things, and what it will not exceed.
///
/// A struct rather than eight parameters, and it exists for testability as much
/// as for readability: the public entry point fills it from [`AgentPaths`] and
/// the constants above, and a test fills it with directories it can really
/// create. Every field is agent-derived — nothing in it comes from a request.
struct Placement {
    /// The directory holding every account's home. `/home` on a real host.
    home_root: PathBuf,
    /// The first directory of the scratch chain this operation is willing to
    /// state — `/` on a real host, so every level of the real path is checked.
    /// A test owns nothing above its temporary directory and passes that
    /// instead, because no test can say anything true about `/tmp`'s mode.
    scratch_base: PathBuf,
    /// The scratch root: this directory and everything under it must be
    /// `0700` and root's, while the levels above it need only be root's and
    /// unwritable by anybody else. [`AgentPaths::BULK_SCRATCH_ROOT`] on a real
    /// host.
    scratch_root: PathBuf,
    /// The root-only scratch this backup stages its dumps and manifest in.
    scratch: PathBuf,
    /// The uid the backup tree and the artifact must belong to.
    owner: u32,
    /// The per-database dump ceiling, in bytes.
    dump_ceiling: u64,
    /// The most members the home may hold, counted by the measuring walk
    /// before any dump is taken. `MAXIMUM_MEMBERS` on a real host: the figure a
    /// restore refuses a listing at, so creation refuses early what restore
    /// would refuse late.
    member_ceiling: u64,
    /// The finished-artifact ceiling, in bytes.
    archive_ceiling: u64,
    /// The free-space floor the backup directory's filesystem must still have
    /// before anything is written (issue #148). The scratch is bounded on its
    /// own, per dump, by [`scratch_dump_ceiling`].
    floor: FreeSpaceFloor,
}

/// Creates one backup of `account` under `root` and answers what it produced.
///
/// # What a completed backup means here
///
/// It means everything: every database the catalog named was dumped and hashed,
/// the manifest lists all of them, `tar` exited zero, the artifact was hashed,
/// and it was published onto its final name by a rename. **A run that got part
/// of the way through produces an error and leaves nothing behind that anything
/// could mistake for a backup.** Files archived with one dump missing is not a
/// smaller backup — it is an artifact a later restore would trust, and would
/// use to put a truncated database over a working one. That is worse than no
/// artifact, and it is why the failure path deletes rather than keeps.
///
/// Concretely, on every error path: the scratch is removed (dumps included, and
/// they are the copy of the customer's data on disk), the `.partial` is
/// removed, no sidecar is written, and the published name is never created.
/// The scratch is removed on the success path too, by the same line.
///
/// # Order of the steps, and why this order
///
/// 1. Take the account's lock, without waiting. Two creations of one account
///    would interleave two `tar` runs over one home; the second is refused as
///    [`BackupError::AlreadyRunning`].
/// 2. Ask the inode whether the backup root is still root's alone
///    (`backup_root`), and create the account's directory under it.
/// 3. Refuse [`BackupError::AlreadyExists`] if an artifact with this id is
///    already published — BEFORE any dump. A retry of a creation whose response
///    was lost must cost a `stat`, not an hour. Then refuse
///    [`BackupError::FreeSpace`] when the backup directory's filesystem is
///    below the free-space floor (issue #148).
/// 4. Measure the home with one directory walk and refuse
///    [`BackupError::ArchiveFailed`] (`LISTING_REFUSED`) when it holds more
///    members than a restore would accept — BEFORE the scratch or any dump
///    exists, so a refused home costs one walk and nothing else.
/// 5. `dumping_databases`: one dump per database, hashed as it lands.
/// 6. `archiving_files`: write the manifest, run the archiver, hash the
///    artifact, and list the finished archive with the bounds a restore
///    applies (the authority, and the only check on the listing's byte cap).
/// 7. Check the artifact's own ownership and mode, then publish it by rename
///    and write the sidecar.
///
/// The `uploading` stage belongs to a remote destination and is not reported
/// here: a local creation does not upload, and reporting a stage that did no
/// work is the kind of progress this product has already been burned by. The
/// terminal 100 belongs to the service layer's terminal message for the same
/// reason — it means "the operation finished", which is a thing only the caller
/// that returns it knows.
///
/// # Errors
///
/// Every variant of [`BackupError`]; each one leaves the account exactly as it
/// was and leaves no publishable artifact.
pub fn create_backup(
    host: &dyn BackupHost,
    catalog: &dyn DatabaseCatalog,
    account: &AccountName,
    backup_id: &BackupId,
    root: &LocalBackupRoot,
    floor: &FreeSpaceFloor,
    sink: &mut dyn ProgressSink,
) -> Result<BackupSummary, BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let directory = prepare_account_directory(root, account)?;
    let placement = Placement {
        home_root: PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT),
        scratch_base: PathBuf::from("/"),
        scratch_root: PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
        scratch: AgentPaths::backup_scratch_dir(backup_id),
        owner: ROOT_UID,
        dump_ceiling: MAXIMUM_DUMP_BYTES,
        member_ceiling: MAXIMUM_MEMBERS as u64,
        archive_ceiling: MAXIMUM_ARCHIVE_BYTES,
        floor: *floor,
    };

    let outcome = create_in(
        &placement, host, catalog, account, backup_id, &directory, sink,
    );

    drop(guard);
    outcome
}

/// The body of [`create_backup`], with every path and ceiling injected.
///
/// Split for the reason `LocalBackupRoot::resolve`'s body is split: every
/// directory a test can create sits under `/tmp` or under a home, which are
/// exactly the places the real paths are not, so an operation that read
/// `AgentPaths` directly could not be exercised at all. This is the function
/// the tests drive; the wrapper above is the one line that says what the real
/// host's answers are.
///
/// # Errors
///
/// As documented on [`create_backup`].
fn create_in(
    placement: &Placement,
    host: &dyn BackupHost,
    catalog: &dyn DatabaseCatalog,
    account: &AccountName,
    backup_id: &BackupId,
    directory: &Path,
    sink: &mut dyn ProgressSink,
) -> Result<BackupSummary, BackupError> {
    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    // Before any work at all: the retry path costs one `stat`.
    if artifact.symlink_metadata().is_ok() {
        return Err(BackupError::AlreadyExists);
    }

    // Then the floor, still before the scratch or any dump exists: an archive
    // that fills the disk part-way is removed again, but by then it has
    // already starved every other writer on that filesystem.
    placement.floor.admit(host.available_bytes(directory))?;

    let mut partial = artifact.clone().into_os_string();
    partial.push(PARTIAL_SUFFIX);
    let partial = PathBuf::from(partial);

    let assembled = assemble(placement, host, catalog, account, backup_id, &partial, sink);

    // Always, on every path: the scratch holds a full dump of the customer's
    // databases, and a scratch that outlives the operation that made it is a
    // copy of their data nobody is watching.
    let _ = remove_dir_all(&placement.scratch);

    let summary = match assembled {
        Ok(summary) => summary,
        Err(error) => {
            let _ = remove_file(&partial);
            return Err(error);
        }
    };

    publish_artifact(&partial, &artifact, placement.owner)?;
    write_sidecar(&sidecar, &artifact, &summary, placement.owner)?;

    Ok(summary)
}

/// Dumps, archives and hashes — everything that happens before publication.
///
/// Separated from [`create_in`] so that the cleanup of the scratch and of the
/// `.partial` is written ONCE, on the outside, rather than at each of the eight
/// places this body can return early. A cleanup repeated per error path is a
/// cleanup that is missing from one of them.
///
/// # Errors
///
/// As documented on [`create_backup`].
fn assemble(
    placement: &Placement,
    host: &dyn BackupHost,
    catalog: &dyn DatabaseCatalog,
    account: &AccountName,
    backup_id: &BackupId,
    partial: &Path,
    sink: &mut dyn ProgressSink,
) -> Result<BackupSummary, BackupError> {
    // First of all, before the scratch exists and before a single dump is
    // taken: one directory walk decides whether a restore could ever accept
    // this home's listing. A home with more members than the ceiling would
    // otherwise be dumped and archived for up to hours and only then be refused
    // by the post-archive listing. The refusal is made only when the walk is
    // SURE (it counts the home itself, and the archive adds just a handful of
    // members), and it reports the same `LISTING_REFUSED` status the late
    // listing would, so the panel reads one error whichever check fires. The
    // post-archive listing stays as the authority, and as the only check on the
    // listing's byte cap.
    let home = placement.home_root.join(account.as_str());
    let measure = measure_home_bytes(&home)?;
    if measure.members > placement.member_ceiling {
        return Err(BackupError::ArchiveFailed {
            status: LISTING_REFUSED,
        });
    }

    let databases_directory = open_backup_scratch(
        &placement.scratch_base,
        &placement.scratch_root,
        &placement.scratch,
        placement.owner,
    )?;

    sink.report(
        BackupStage::DumpingDatabases,
        BackupStage::DumpingDatabases.start_percent(),
    );
    let names = catalog.databases_of(account)?;
    let total = names.len() as u64;
    let mut databases: Vec<ManifestDatabase> = Vec::with_capacity(names.len());
    for (index, name) in names.iter().enumerate() {
        // Re-measured for THIS dump, not once for the loop: nothing removes an
        // individual dump, so every earlier one is still resident and the room
        // left is smaller than it was. A ceiling computed once and applied `N`
        // times bounds `N` times itself.
        let ceiling = scratch_dump_ceiling(&databases_directory, placement.dump_ceiling)?;
        databases.push(dump_database(host, name, &databases_directory, ceiling)?);
        sink.report(
            BackupStage::DumpingDatabases,
            BackupStage::DumpingDatabases.percent_through(index as u64 + 1, total),
        );
    }

    let manifest = BackupManifest {
        version: MANIFEST_VERSION,
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        created_at_unix: host.now_unix(),
        home_bytes: measure.bytes,
        databases,
        agent_version: env!("CARGO_PKG_VERSION").to_owned(),
    };

    sink.report(
        BackupStage::ArchivingFiles,
        BackupStage::ArchivingFiles.start_percent(),
    );
    create_partial(partial)?;
    archive_home(
        host,
        &manifest,
        &ArchiveSpec {
            home,
            scratch: placement.scratch.clone(),
            artifact: partial.to_path_buf(),
        },
    )?;

    let (artifact_sha256, artifact_bytes) = checksum_file(partial)?;
    if artifact_bytes > placement.archive_ceiling {
        return Err(BackupError::ArchiveTooLarge {
            limit: placement.archive_ceiling,
            actual: artifact_bytes,
        });
    }
    sink.report(
        BackupStage::ArchivingFiles,
        BackupStage::ArchivingFiles.end_percent(),
    );

    Ok(BackupSummary::readable(
        backup_id.as_str().to_owned(),
        manifest,
        artifact_bytes,
        artifact_sha256,
    ))
}

#[cfg(test)]
#[path = "../tests/backup/create_backup_tests.rs"]
mod tests;
