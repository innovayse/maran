//! CreateBackup: one archive of an account's home and databases.

use std::fs::{DirBuilder, File, remove_dir_all, remove_file, rename};
use std::io::Write as _;
use std::os::unix::fs::{DirBuilderExt as _, MetadataExt as _, OpenOptionsExt as _};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::backup::archive::archive_home::archive_home;
use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::dump_database::dump_database;
use crate::backup::archive::measure_home::measure_home_bytes;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_lock::take_account_lock;
use crate::backup::backup_root::prepare_account_directory;
use crate::backup::database_catalog::DatabaseCatalog;
use crate::backup::model::archive_spec::ArchiveSpec;
use crate::backup::model::backup_manifest::{BackupManifest, MANIFEST_VERSION};
use crate::backup::model::backup_stage::BackupStage;
use crate::backup::model::backup_summary::BackupSummary;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::model::progress_sink::ProgressSink;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::root_only_chain::{MissingLevels, SCRATCH_MODE, require_root_only_chain};
use crate::backup::scratch_dump_ceiling::scratch_dump_ceiling;

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

/// The scratch's directory of dumps — the archive's `databases/` member.
const DATABASES_DIRECTORY: &str = "databases";

/// The mode the artifact and its sidecar are created with, and the only one
/// they may carry when they are published.
///
/// `0600`, root's alone, and it is a rules/security.md item 8 decision rather
/// than a convenience. The artifact holds every file in the customer's home and
/// a full dump of every database they own — which is to say every credential,
/// token and personal record their application stores. The directory above it
/// is already root-only, so this is the second of two locks, not the first; it
/// is here because a directory's mode is one `chmod` away from being wrong and
/// the artifact is the thing that must not be readable when that happens. The
/// owner is root for the same reason it is not the account: an account that
/// could read its own artifact could replace it, and a restore reads it as
/// root.
const ARTIFACT_MODE: u32 = 0o600;

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
    /// The finished-artifact ceiling, in bytes.
    archive_ceiling: u64,
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
///    was lost must cost a `stat`, not an hour.
/// 4. `dumping_databases`: one dump per database, hashed as it lands.
/// 5. `archiving_files`: measure the home, write the manifest, run the
///    archiver, hash the artifact.
/// 6. Check the artifact's own ownership and mode, then publish it by rename
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
        archive_ceiling: MAXIMUM_ARCHIVE_BYTES,
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

    publish(&partial, &artifact, placement.owner)?;
    write_sidecar(&sidecar, &summary, placement.owner)?;

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
    let databases_directory = open_scratch(
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

    let home = placement.home_root.join(account.as_str());
    let manifest = BackupManifest {
        version: MANIFEST_VERSION,
        account: account.as_str().to_owned(),
        backup_id: backup_id.as_str().to_owned(),
        created_at_unix: host.now_unix(),
        home_bytes: measure_home_bytes(&home)?,
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

/// Creates the root-only scratch and its `databases/` directory, and answers
/// the latter — after proving, against the inodes, that the place it is about
/// to create them in is root's alone all the way up.
///
/// Anything left by a crashed earlier run under this id is removed first: the
/// id is this agent's own and unique to the operation, so what is there can
/// only be its own litter — and a stale dump left in place would be archived as
/// if it belonged to this backup.
///
/// # Why the chain is checked and not just the leaf
///
/// The modes this creates are `0700 root:root` at every level, and they were
/// measured to hold on a chain this code built end to end. They are still not a
/// boundary while somebody else owns an ancestor: the owner of a directory can
/// rename an entry inside it aside and put a symlink in its place *without ever
/// having permission to enter it*, and the next write from root then follows
/// the link. That was measured both ways round — a customer's plaintext dump
/// delivered into an attacker-readable file, and a root-owned `0600` file
/// truncated and overwritten — in
/// `docs/superpowers/notes/2026-09-05-backups-threat-note.md` §1, back when
/// [`AgentPaths::BULK_SCRATCH_ROOT`] lived inside the `panel`-owned
/// `/var/lib/maran`.
///
/// Moving the root out of that tree is the primary fix and this check is the
/// second one, for the same reason
/// [`prepare_account_directory`](crate::backup::backup_root) states a mode it
/// just created: a location nobody can reach today is out of reach only for as
/// long as an installer, an operator or a later version keeps it that way, and
/// a mode that is assumed reports nothing when it stops being true. The check
/// runs against the path the dumps are actually written into, not against the
/// constant they were derived from.
///
/// # Errors
///
/// [`BackupError::ScratchUnusable`] — for a chain that could not be created,
/// and for one this agent refuses to write into. The two are one variant
/// because the answer to both is the same: nothing was staged, and the
/// operation stops before a single dump exists.
///
/// # Why the base, the root and the owner are parameters
///
/// For the reason `backup_root.rs` splits its own checks: a test does not run
/// as uid 0 and cannot create a directory owned by root, so a gate that
/// hard-coded root could only ever be exercised on its refusing path — and a
/// gate that has only ever been handed input it must reject passes just as
/// well once it has been mutated into rejecting everything (rules/testing.md,
/// "a refusing gate needs an inverse control"). Injected, a test can hand it a
/// chain it must ACCEPT and a chain it must refuse, and the refusal it
/// observes is the code the daemon runs. [`create_backup`] fills all three
/// from [`AgentPaths`] and [`ROOT_UID`]; nothing in them comes from a request.
fn open_scratch(
    base: &Path,
    root: &Path,
    scratch: &Path,
    owner: u32,
) -> Result<PathBuf, BackupError> {
    // Before anything is created: what already exists on the way down must
    // already be root's. A level that does not exist yet is passed over here
    // and stated again below, once this call has made it.
    require_root_only_chain(base, root, scratch, owner, MissingLevels::Tolerated)?;

    let _ = remove_dir_all(scratch);

    let databases = scratch.join(DATABASES_DIRECTORY);
    DirBuilder::new()
        .recursive(true)
        .mode(SCRATCH_MODE)
        .create(&databases)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // And again on the finished chain, including the directory the dumps go in:
    // the same TOCTOU re-assertion `restore_backup::open_scratch` makes, for the
    // same reason and with the same blind spot, and worded the same way so the
    // twins cannot drift apart.
    //
    // It is not, as this comment used to claim, a check on a directory this call
    // did not create: such a directory is already refused by the arm above, and
    // `databases` here was made by the `DirBuilder` two lines up with
    // `SCRATCH_MODE`, which a umask can only tighten. What is left is the window
    // between the check above and that `create` — a level renamed aside and
    // replaced inside it.
    //
    // That window is a race, so NO TEST OBSERVES THIS ARM (ledger S7: the
    // mutation removing it survives the whole suite). Kept, not deleted:
    // rules/testing.md deletes a defensive call that CANNOT FAIL, and this one
    // can — what cannot happen is the test.
    require_root_only_chain(base, root, &databases, owner, MissingLevels::Refused)?;

    Ok(databases)
}

/// Creates the `.partial` the archiver writes, mode `0600`, and refuses a name
/// that is already taken.
///
/// `create_new`, so the file is made by this call or not at all: the archiver
/// is handed a descriptor-backed name this operation created with the mode it
/// chose, rather than opening whatever is at that path. A `.partial` left by a
/// crashed run is removed by the operation's own cleanup, and one that is still
/// there at this moment means something else is writing it — which the account
/// lock says cannot be another backup.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
fn create_partial(partial: &Path) -> Result<(), BackupError> {
    let _ = remove_file(partial);

    File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(partial)
        .map(drop)
        .map_err(|_| BackupError::ArtifactUnpublishable)
}

/// Checks the finished artifact's own inode and publishes it by rename.
///
/// The mode and owner are checked HERE, on the file the archiver actually
/// wrote, and not assumed from the mode it was created with: `tar` was handed a
/// path, and what a path meant when the file was created is not what it means
/// now. An artifact that is not root's alone is not published at all — the
/// rename would make a readable copy of a customer's database the panel then
/// advertises as a backup.
///
/// `nlink` is checked for the same reason the file operations area checks it: a
/// second hard link to the artifact is a second name for the same bytes, and
/// the mode on this name says nothing about who can open the other one.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
fn publish(partial: &Path, artifact: &Path, owner: u32) -> Result<(), BackupError> {
    let metadata = partial
        .symlink_metadata()
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    let safe = metadata.is_file()
        && metadata.uid() == owner
        && metadata.mode() & 0o7777 == ARTIFACT_MODE
        && metadata.nlink() == 1;
    if !safe {
        return Err(BackupError::ArtifactUnpublishable);
    }

    rename(partial, artifact).map_err(|_| BackupError::ArtifactUnpublishable)
}

/// Writes the sidecar beside the published artifact, mode `0600`, on an inode
/// this call created and then re-stated.
///
/// The sidecar is the same document the archive carries at its root, plus the
/// artifact's own digest and size. Two copies on purpose: a listing reads the
/// sidecar without decompressing gigabytes, and a restore reads the copy INSIDE
/// the archive and refuses when the two disagree — which is what makes editing
/// the cheap copy useless to anybody who tries it.
///
/// # Why the artifact's discipline, not a lighter one
///
/// Written forward from the question — *what does a reader of this file
/// decide?* — and the answer is: everything. `list_backups` reads it to say
/// what backups exist and which of them are readable, and `restore_backup`
/// reads it for the size and digest it checks the artifact against. A sidecar
/// somebody else placed is therefore not a wrong label on a right backup; it
/// is a backup an attacker gets to describe, which is the same class of
/// authority the artifact itself carries. So it gets the same two guarantees
/// the artifact gets, for the same reasons and not by analogy:
///
/// - **`create_new`**, so the inode is made by this call or the write does not
///   happen. `create` opens whatever is at the path — a symlink is FOLLOWED,
///   and `mode` applies only to a file that is created, so a pre-placed link
///   or a pre-placed `0644` file would be written through with the customer's
///   database inventory and keep its own mode. `create_new` cannot follow a
///   symlink: `O_EXCL` makes the open fail on any existing name, link
///   included.
/// - **A re-`stat` of the result** (`is_file`, uid, exact mode, `nlink == 1`),
///   for the reason [`publish`] states: what a path meant when the file was
///   created is not what it means now, and `nlink > 1` means a second name for
///   these bytes whose mode this one says nothing about.
///
/// **Why not the `.partial`-and-rename this file's artifact uses.** The
/// artifact renames because `tar` writes for minutes or hours and a reader
/// must never see a half-written archive. The sidecar is one small buffered
/// write, and a rename would actively DESTROY the property being bought here:
/// `rename` does not follow a symlink at its destination — it replaces it,
/// silently, so a pre-placed link would be quietly discarded rather than
/// refused, and the operator would never learn somebody had been in the
/// directory. Refusing is worth more here than atomicity is.
///
/// An unlink-then-create (which [`create_partial`] does) is refused for the
/// same reason: at this point the artifact has just been published under an id
/// no other backup will ever be minted with, so anything already wearing this
/// name is not our own litter and removing it is destroying evidence.
///
/// A refusal after publication leaves a published artifact with no sidecar.
/// That is the state `list_backups` reports as `unreadable` and
/// `delete_backup` prunes — visible litter with a name, not a silent one.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`], because a published artifact
/// nothing can describe is not a backup the panel may report.
fn write_sidecar(sidecar: &Path, summary: &BackupSummary, owner: u32) -> Result<(), BackupError> {
    let rendered =
        serde_json::to_vec_pretty(summary).map_err(|_| BackupError::ArtifactUnpublishable)?;

    let mut file = File::options()
        .write(true)
        .create_new(true)
        .mode(ARTIFACT_MODE)
        .open(sidecar)
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    file.write_all(&rendered)
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    file.sync_all()
        .map_err(|_| BackupError::ArtifactUnpublishable)?;

    verify_sidecar_inode(sidecar, owner)
}

/// Asks the inode what the sidecar this call just wrote actually is, and
/// removes it rather than leaving it when the answer is wrong.
///
/// Split from [`write_sidecar`] so the descriptor is closed before the path is
/// stated — the same question [`publish`] asks, asked of a file that has no
/// rename to gate it, so the removal takes the rename's place as the thing
/// that stops a wrong inode being advertised as this backup's description.
///
/// # Errors
///
/// [`BackupError::ArtifactUnpublishable`].
fn verify_sidecar_inode(sidecar: &Path, owner: u32) -> Result<(), BackupError> {
    let metadata = sidecar
        .symlink_metadata()
        .map_err(|_| BackupError::ArtifactUnpublishable)?;
    let safe = metadata.is_file()
        && metadata.uid() == owner
        && metadata.mode() & 0o7777 == ARTIFACT_MODE
        && metadata.nlink() == 1;
    if !safe {
        let _ = remove_file(sidecar);
        return Err(BackupError::ArtifactUnpublishable);
    }

    Ok(())
}

#[cfg(test)]
#[path = "../tests/backup/create_backup_tests.rs"]
mod tests;
