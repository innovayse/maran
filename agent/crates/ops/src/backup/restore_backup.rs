//! RestoreBackup: putting an archive back over a live account.

use std::fs::{DirBuilder, remove_dir_all, rename};
use std::os::unix::fs::{DirBuilderExt as _, PermissionsExt as _, chown};
use std::path::{Path, PathBuf};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::system::backup_id::BackupId;
use maran_agent_core::validation::system::local_backup_root::LocalBackupRoot;
use maran_agent_core::validation::system::name::AccountName;

use crate::accounts::take_account_lock;
use crate::backup::archive::checksum_file::checksum_file;
use crate::backup::archive::dump_database::dump_database;
use crate::backup::archive::extract_databases_as_root::{
    extract_databases_as_root, extracted_dump_path,
};
use crate::backup::archive::extract_home_as_account::extract_home_as_account;
use crate::backup::archive::read_manifest::read_manifest;
use crate::backup::archive::replace_database::replace_database;
use crate::backup::archive::scan_members::{refused_member_name, scan_members};
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::backup_root::prepare_account_directory;
use crate::backup::model::backup_manifest::BackupManifest;
use crate::backup::model::manifest_database::ManifestDatabase;
use crate::backup::model::restore_outcome::RestoreOutcome;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::object_key::{artifact_file_name, sidecar_file_name};
use crate::backup::read_sidecar::read_sidecar;
use crate::backup::require_scratch_room::require_scratch_room;
use crate::backup::restore_marker_file::{
    remove_restore_marker, restore_marker_path, write_restore_marker,
};
use crate::backup::root_only_chain::{MissingLevels, SCRATCH_MODE, require_root_only_chain};
use crate::backup::scratch_dump_ceiling::scratch_dump_ceiling;

/// The largest rollback dump this agent will take, per database.
///
/// The same cap the creation side puts on a dump, and for the same reason: a
/// rollback dump is an ordinary dump, and a full disk halfway through taking
/// one is a host that then fails at everything else.
///
/// A cap and not the ceiling. The effective ceiling for each rollback dump is
/// the smaller of this number and nine tenths of what the scratch has left when
/// that dump is taken, and this side needs that far more than the creation side
/// does: a restore's peak is every extracted archive dump PLUS every rollback
/// dump taken so far, all coexisting in one scratch, so a per-dump constant
/// bounds `2 · N · C` and not `C`. The refusal that happens before any of it is
/// [`require_scratch_room`], applied before the first database is dropped.
const MAXIMUM_DUMP_BYTES: u64 = 8 * 1024 * 1024 * 1024;

/// The scratch's directory of pre-drop rollback dumps.
///
/// Beside `databases/` rather than inside it, so that a rollback dump can never
/// be mistaken for one the archive supplied — they are dumps of opposite
/// provenance and only one of them has a manifest digest.
const ROLLBACK_DIRECTORY: &str = "rollback";

/// The uid the scratch chain belongs to, at every level.
const ROOT_UID: u32 = 0;

/// The mode the account's home directory carries.
const HOME_MODE: u32 = 0o750;

/// Where one restore puts things, and whose the result must be.
///
/// A struct rather than seven parameters, and it exists for testability as much
/// as for readability: the public entry point fills it from [`AgentPaths`] and
/// the constant above, and a test fills it with directories it can really
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
    /// The root-only scratch this restore stages dumps in.
    scratch: PathBuf,
    /// The uid every level of the scratch chain must belong to.
    ///
    /// Deliberately NOT [`Self::owner`], which is the account's own uid: the
    /// staging tree is the account's while the account fills it, and the
    /// scratch is root's and never anybody else's — the archive's database
    /// dumps are extracted into it as root. One field for both would be one
    /// field saying two things, and the one it would end up saying is the
    /// account's.
    scratch_owner: u32,
    /// Where the replacement home is built before it becomes the home.
    staging: PathBuf,
    /// Where the home being replaced is parked until the restore has finished.
    previous: PathBuf,
    /// The document written immediately before the first rename of the swap and
    /// removed after the last step, so that a process which never saw this
    /// request can finish what a killed one started. See
    /// `crate::backup::recover_restores`.
    marker: PathBuf,
    /// The uid the restored home root must end up owned by, and the uid the
    /// staging tree is handed to before it is filled.
    owner: u32,
    /// The account's own gid, which the staging tree is handed to.
    ///
    /// Not the same as [`Self::group`], and the difference is the whole point
    /// of having both: the staging tree belongs to the account while the
    /// account is writing into it, and the finished home belongs to the web
    /// server's group so that the server can traverse it.
    account_group: u32,
    /// The gid the restored home root must end up owned by — the web server's
    /// group, resolved one layer up from the distro adapter.
    group: u32,
}

/// Restores `account` from the artifact `backup_id` names, replacing its home
/// and the databases the panel still knows about.
///
/// # If this fails halfway, what state is the account in, and can the customer
/// still work?
///
/// That question is what this operation is designed around, and it is answered
/// step by step below rather than left for a reader to reconstruct. It has a
/// single dividing line: **the first `DROP DATABASE` is the point of no
/// return.** Everything before it is undone by doing nothing at all —
/// no rename has happened, no database has been touched, and the only traces
/// are directories under the agent's own scratch and staging roots. Everything
/// after it is undone only by reloading a dump this operation took moments
/// earlier, and reloading can itself fail.
///
/// | # | Step | If it fails HERE, the account is |
/// |---|---|---|
/// | 1 | Verify the artifact's SHA-256 against the digest the panel recorded | Untouched. The customer keeps working; nothing was read into anything. |
/// | 2 | Pre-scan the archive's member list and refuse an archive whose layout is not ours | Untouched. Nothing has been unpacked — see "the hostile archive" below. |
/// | 3 | Extract `manifest.json` root-side; refuse an unknown version, another account's archive, or a manifest that disagrees with the sidecar; refuse a database the panel no longer knows | Untouched. |
/// | 4 | Extract `databases/*` root-side into the root-only scratch and check each dump against the manifest's digest | Untouched. |
/// | 5 | Extract `home/*` AS the account into the staging directory | Untouched. The staging tree is removed on the way out. |
/// | 6 | **For each database in turn: dump it for rollback, then drop it, then load the archive's dump.** The first drop is the point of no return | Every database already replaced is reloaded from its own rollback dump, in reverse order, and the operation reports FAILED naming what it put back and what it could not. **The files are still untouched** — the swap has not happened yet, so there is nothing to reverse there. |
/// | 7 | Rename the home aside, then rename the staging tree into its place | Between the two renames the account has no home, for microseconds. If the second rename fails the first is reversed at once and the account has its home back. If the reversal also fails, the error names the exact path the home is parked at, because a message an operator can act on beats a rollback that lies. |
/// | 8 | Re-apply ownership and mode on the home root; remove the parked tree and the scratch | Restored. A failure here is a disk-space or a permissions problem to report, not a failed restore — except the ownership, which is re-applied before anything is removed for the reason in "finalising" below. |
///
/// # Why the rollback dump is taken per database, immediately before its own
/// drop — and not for all of them up front
///
/// Dumping all five before dropping any looks tidier and is worse. A dump is a
/// point in time, and the value of a rollback dump is that it is the state the
/// database was in **at the moment it was dropped**. Taken up front, the fifth
/// database's rollback dump is as old as the first four replacements took —
/// minutes on a real account — and every write the customer's application made
/// in that window is silently lost by the rollback that was supposed to make
/// them whole. It also fails in the wrong direction: taking five dumps first
/// means a failure on the fifth dump has already done nothing, which is fine,
/// but a failure on the fifth *load* rolls back to five stale points instead of
/// five exact ones. Interleaved, each database's rollback dump is separated
/// from its own drop by one call and by nothing else.
///
/// # What this does NOT restore
///
/// A backup here is files plus databases, and a restore is therefore files plus
/// databases. **It is not an undo.** Specifically, it does not re-create or
/// revert:
///
/// - **nginx vhosts and the site rows behind them** — a site deleted since the
///   backup was taken stays deleted, and a site whose configuration changed
///   keeps the new configuration, serving the restored files.
/// - **TLS certificates** — these live outside the home, in the agent's own
///   certificate directory, and a certificate replaced or purged since the
///   backup stays replaced or purged.
///
/// Nor does it restore the account itself, its SFTP logins, its cron entries'
/// panel rows, or anything else whose truth is a row in the panel's database
/// rather than a file in the home. Those are named here, and not only in the
/// UI, because the failure this warning exists against is a reader of THIS
/// function concluding that "restore" means "put everything back the way it
/// was" and building a feature on that belief.
///
/// # The hostile archive: which mechanism refuses it, and which is behind it
///
/// A member named `../../etc/cron.d/pwn` is refused by the **pre-scan** in step
/// 2, before `tar` is asked to extract anything at all. The pre-scan is the
/// brace: it reads names only, judges the whole archive, and refuses it entire
/// — so not one member of an archive holding that name is unpacked, including
/// the members that looked ordinary.
///
/// `tar`'s own stripping of a leading `/` and its refusal of a `..` component
/// are the **belt behind that brace**: a second, per-member defence, built by
/// somebody else, that this agent guarantees stays armed by never passing
/// `--absolute-names` — `ExtractSpec` has no field that could. The belt is not
/// the reason the brace can be relaxed; it is the reason a defect in the brace
/// is not immediately a root write at a path an attacker named.
///
/// # What this operation deliberately does not do
///
/// It does **not** take the pre-restore backup that Decision 2 puts at step 2
/// of the sequence. That step is a whole `create_backup`, which needs the
/// database catalog seam, and composing two operations is the service layer's
/// job in this crate, not an operation's — `ops::backup` does not call
/// `ops::db` and does not call itself. The caller takes it before calling here.
/// This is written down rather than assumed, because a reader who believed the
/// pre-restore backup happened inside this function would have no reason to
/// check that anyone takes it.
///
/// # Errors
///
/// Every restore-side variant of [`BackupError`]. The ones that matter to a
/// customer are [`BackupError::RolledBack`] (the databases are as they were),
/// [`BackupError::RolledBackPartially`] (some are neither state, and it names
/// them) and [`BackupError::HomeParkedAt`] (the home exists, at the path in the
/// message).
// Eight parameters, and each one is a different authority. Folding them into a
// request struct was considered and rejected: the struct would be built by the
// service layer, one field at a time, and a field left at its default there is
// exactly the kind of omission this operation must not have — an empty
// `allowed_databases` would silently restore nothing, and a blank
// `expected_sha256` would fail closed only by luck. Named parameters cannot be
// forgotten.
#[allow(clippy::too_many_arguments)]
pub fn restore_backup(
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    root: &LocalBackupRoot,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    home_group: u32,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let guard = take_account_lock(account).ok_or(BackupError::AlreadyRunning)?;

    let directory = prepare_account_directory(root, account)?;
    // Asked through the host seam rather than through `AccountIds::resolve`
    // directly, because it is asked a SECOND time inside `perform` and the two
    // answers are compared. A question asked twice has to be askable of a
    // test.
    let ids = host.account_identity(account);
    let outcome = ids.and_then(|ids| {
        let placement = Placement {
            home_root: PathBuf::from(AgentPaths::ACCOUNT_HOME_ROOT),
            scratch_base: PathBuf::from("/"),
            scratch_root: PathBuf::from(AgentPaths::BULK_SCRATCH_ROOT),
            scratch: AgentPaths::backup_scratch_dir(backup_id),
            scratch_owner: ROOT_UID,
            staging: AgentPaths::restore_staging_dir(account, backup_id),
            previous: AgentPaths::restore_previous_dir(account, backup_id),
            marker: restore_marker_path(account, backup_id),
            owner: ids.uid,
            account_group: ids.gid,
            group: home_group,
        };

        restore_in(
            &placement,
            host,
            account,
            backup_id,
            &directory,
            allowed_databases,
            expected_sha256,
            sink,
        )
    });

    drop(guard);
    outcome
}

/// The body of [`restore_backup`], with every path and id injected.
///
/// Split for the reason the creation's body is split: every directory a test
/// can create sits under `/tmp` or under a home, which are exactly the places
/// the real paths are not, so an operation that read [`AgentPaths`] directly
/// could not be exercised at all.
///
/// # Errors
///
/// As documented on [`restore_backup`].
#[allow(clippy::too_many_arguments)]
fn restore_in(
    placement: &Placement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    directory: &Path,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let artifact = directory.join(artifact_file_name(backup_id));
    let sidecar = directory.join(sidecar_file_name(backup_id));

    let prepared = prepare(
        placement,
        host,
        account,
        &artifact,
        &sidecar,
        allowed_databases,
        expected_sha256,
        sink,
    );

    // The staging tree is removed on every path that did not swap it into
    // place, and the scratch on every path at all — written ONCE, on the
    // outside, rather than at each of the dozen places the body below can
    // return early. A cleanup repeated per error path is a cleanup that is
    // missing from one of them, and the scratch holds a full copy of the
    // customer's databases.
    let planned = match prepared {
        Ok(planned) => planned,
        Err(error) => {
            let _ = remove_dir_all(&placement.staging);
            let _ = remove_dir_all(&placement.scratch);
            return Err(error);
        }
    };

    let outcome = perform(placement, host, account, backup_id, &planned, sink);

    let _ = remove_dir_all(&placement.staging);
    let _ = remove_dir_all(&placement.scratch);

    outcome
}

/// What verification decided: which databases to replace, and from which
/// manifest entry.
///
/// Built only by [`prepare`], which is the only thing that has checked any of
/// it. A value of this type means every refusal in steps 1 to 5 has already
/// been made — which is what lets [`perform`] be read as "and now the
/// irreversible half", with no verification mixed into it.
struct Planned {
    /// Every database to replace, paired with the manifest entry that
    /// describes its dump, in the manifest's own order.
    databases: Vec<(DatabaseName, ManifestDatabase)>,
}

/// Steps 1 to 5: everything whose failure leaves the account exactly as it was.
///
/// The stage boundary and the recoverability boundary are the SAME boundary,
/// which is why all of this is reported as `verifying` even though step 5
/// really does write a whole home to disk. Nothing it writes is anywhere the
/// account can see, and every one of these returns undoes itself by the two
/// `remove_dir_all`s the caller runs unconditionally. The first report that is
/// not `verifying` is the first report after which doing nothing is no longer a
/// recovery.
///
/// # Errors
///
/// As documented on [`restore_backup`], for steps 1 to 5.
#[allow(clippy::too_many_arguments)]
fn prepare(
    placement: &Placement,
    host: &dyn BackupHost,
    account: &AccountName,
    artifact: &Path,
    sidecar: &Path,
    allowed_databases: &[DatabaseName],
    expected_sha256: &str,
    sink: &mut dyn RestoreSink,
) -> Result<Planned, BackupError> {
    sink.report(
        RestoreStage::Verifying,
        RestoreStage::Verifying.start_percent(),
    );

    if artifact.symlink_metadata().is_err() {
        return Err(BackupError::NotFound);
    }

    // Step 1. Before the archive is opened for any purpose: are these the bytes
    // the panel took? Everything after this point treats the archive as this
    // agent's own output, and that treatment has to be earned first.
    let (actual_sha256, _bytes) = checksum_file(artifact)?;
    if actual_sha256 != expected_sha256 {
        return Err(BackupError::ChecksumMismatch);
    }

    let scratch = open_scratch(
        &placement.scratch_base,
        &placement.scratch_root,
        &placement.scratch,
        placement.scratch_owner,
    )?;

    // Step 2. Names only, and the whole archive judged before any of it is
    // unpacked.
    scan_members(host, artifact)?;

    // Step 3.
    let manifest = read_manifest(host, artifact, &scratch, account.as_str())?;
    require_sidecar_agrees(sidecar, &manifest, &actual_sha256)?;
    let databases = resolve_databases(&manifest, allowed_databases)?;

    // Step 4.
    extract_databases_as_root(host, artifact, &scratch, &databases)?;

    // Step 5.
    extract_home_as_account(
        host,
        artifact,
        &placement.staging,
        account,
        placement.owner,
        placement.account_group,
    )?;

    sink.report(
        RestoreStage::Verifying,
        RestoreStage::Verifying.end_percent(),
    );

    Ok(Planned { databases })
}

/// Steps 6 to 8: the irreversible half.
///
/// # Errors
///
/// As documented on [`restore_backup`], for steps 6 to 8.
fn perform(
    placement: &Placement,
    host: &dyn BackupHost,
    account: &AccountName,
    backup_id: &BackupId,
    planned: &Planned,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let total = u32::try_from(planned.databases.len()).unwrap_or(u32::MAX);
    let restored = replace_databases(placement, host, planned, sink)?;

    // The account's identity, asked again — and this is the last moment at
    // which asking it is worth anything, because the next three statements
    // write a marker naming a uid, rename a home into place, and chown it.
    //
    // The uid in `placement` was read when this operation started, which for a
    // large account was hours ago. It is a fact about that moment: `userdel`
    // frees a uid and `useradd` hands the lowest free one to the next account
    // created on this host, so a home chowned to a remembered number can land
    // under a DIFFERENT customer — and the agent's own ownership check compares
    // uids, so every later file operation would agree that the new tenant owns
    // the old one's files.
    //
    // The account lock this operation holds excludes this agent's own
    // deletion, so what this catches is what the lock cannot see: a `userdel`
    // an operator ran by hand, or a second agent binary. It refuses BEFORE the
    // marker and before the first rename, which is the recoverable side of the
    // line — the staging tree is removed by the cleanup the caller runs
    // unconditionally, and the account's home has not been touched.
    let current = host.account_identity(account)?;
    if current.uid != placement.owner || current.gid != placement.account_group {
        return Err(BackupError::AccountIdentityChanged);
    }

    // Written here and nowhere else: AFTER every database has been replaced and
    // BEFORE the first rename. Its position is what gives it its meaning — a
    // marker on the disk is the fact that step 6 finished, which is why a
    // startup reconciliation finishing the swap FORWARD is completing the
    // operation the customer asked for rather than guessing at one. See
    // `recover_restores` and the threat note it names.
    write_restore_marker(
        &placement.marker,
        account,
        backup_id,
        placement.owner,
        placement.group,
        HOME_MODE,
    )?;

    swap_home(placement, account, sink)?;
    finalise(placement, account, sink)?;

    // Last, and only on the fully successful path. Removing it earlier would
    // hand the window back to the defect this whole marker exists to close; not
    // removing it at all is harmless, because the next start classifies the
    // swap as `Completed` and removes it then.
    remove_restore_marker(&placement.marker);

    Ok(RestoreOutcome {
        files_restored: true,
        databases_restored: restored,
        databases_total: total,
    })
}

/// Step 6: each database in turn — rollback dump, drop, load.
///
/// Answers how many were replaced, which on this function's success is always
/// every one of them: a partial pass returns an error, never a smaller number.
/// That is R7 made structural. A restore that "restored four of five" and
/// reported success is the account-deletion cascade's defect with a different
/// noun, and the way to make it unreportable is to have no success value that
/// can describe it — the count [`RestoreOutcome`] carries is the count this
/// function returns, and this function returns only `total`.
///
/// # Errors
///
/// [`BackupError::RolledBack`] or [`BackupError::RolledBackPartially`] for a
/// failure at or after the first drop, and the dump client's own errors for a
/// failure taking the very first rollback dump — which happens before any drop
/// and therefore leaves everything untouched.
fn replace_databases(
    placement: &Placement,
    host: &dyn BackupHost,
    planned: &Planned,
    sink: &mut dyn RestoreSink,
) -> Result<u32, BackupError> {
    let rollback = placement.scratch.join(ROLLBACK_DIRECTORY);
    DirBuilder::new()
        .recursive(true)
        .mode(0o700)
        .create(&rollback)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // BEFORE the first drop, while everything is still untouched: can this
    // filesystem hold the rollback dumps at all? Every rollback dump taken
    // during this restore stays resident until the operation ends, so the
    // requirement is their sum and not one of them.
    //
    // The figures are the ARCHIVE's dump sizes, which is an estimate of the
    // live databases' — the only one available before the dumps are taken. It
    // is the right order of magnitude, and being approximately right before a
    // single database is dropped beats being exactly right afterwards.
    let required: u64 = planned
        .databases
        .iter()
        .map(|(_name, entry)| entry.bytes)
        .fold(0, u64::saturating_add);
    require_scratch_room(&rollback, required)?;

    let total = planned.databases.len() as u64;
    sink.report(
        RestoreStage::RestoringDatabases,
        RestoreStage::RestoringDatabases.start_percent(),
    );

    let mut replaced: Vec<&DatabaseName> = Vec::with_capacity(planned.databases.len());
    for (index, (name, _entry)) in planned.databases.iter().enumerate() {
        // Immediately before THIS database's own drop, and not with the others:
        // the dump's whole value is that it is the state the database was in at
        // the moment it was dropped. See the paragraph on `restore_backup`.
        // Re-measured per dump for the reason the creation side gives: the
        // earlier rollback dumps and every extracted archive dump are still
        // resident in this same scratch.
        let ceiling = match scratch_dump_ceiling(&rollback, MAXIMUM_DUMP_BYTES) {
            Ok(ceiling) => ceiling,
            Err(error) => return Err(unwind(host, &replaced, &rollback, name, error)),
        };
        if let Err(error) = dump_database(host, name, &rollback, ceiling) {
            // Nothing has been dropped for this database yet, so this is a
            // failure on the recoverable side of the line for it — but not for
            // the ones already replaced.
            return Err(unwind(host, &replaced, &rollback, name, error));
        }

        if let Err(error) =
            replace_database(host, name, &extracted_dump_path(&placement.scratch, name))
        {
            replaced.push(name);
            return Err(unwind(host, &replaced, &rollback, name, error));
        }

        replaced.push(name);
        sink.report(
            RestoreStage::RestoringDatabases,
            RestoreStage::RestoringDatabases.percent_through(index as u64 + 1, total),
        );
    }

    Ok(u32::try_from(planned.databases.len()).unwrap_or(u32::MAX))
}

/// Puts every database this restore had already replaced back from its own
/// rollback dump, and says how far it got.
///
/// **In reverse order**, which is not symmetry for its own sake: the databases
/// were replaced in the manifest's order, and an application's schema
/// dependencies point the way that order was built, so undoing them in the
/// order they were made is the order that leaves the fewest moments where a
/// half-undone set references a database that is currently absent.
///
/// The rollback of one database is the same operation as its replacement — drop
/// and load — pointed at the other dump. Deliberately so: a rollback that used
/// a different mechanism from the thing it is undoing would be a second code
/// path exercised only in the situation nobody can reproduce.
///
/// A database whose rollback fails is recorded and the loop CONTINUES. Stopping
/// would leave databases dropped whose rollback dump is sitting on the disk,
/// unread, for no reason but the order they happened to be in.
///
/// **Both lists go into the returned error, and that is the only way out.**
/// This function used to fill a [`RestoreOutcome`] and then return a
/// [`BackupError`], dropping the value it had just built: `restore_backup`
/// answers `Err` on every failing path and no outcome at all, so
/// `rolled_back` was written where nothing could read it. A mutation that
/// emptied it left the whole workspace green, which is what an unreadable
/// field looks like from the outside. The two `Vec`s below are local because
/// their destination is the error's own fields.
///
/// The failure that brought us here is deliberately discarded rather than
/// nested inside the returned one: what an operator needs is which databases
/// are in which state, and [`BackupError`]'s restore variants carry exactly
/// that. The original condition is in the operation's own log line.
fn unwind(
    host: &dyn BackupHost,
    replaced: &[&DatabaseName],
    rollback: &Path,
    failed: &DatabaseName,
    _cause: BackupError,
) -> BackupError {
    let mut rolled_back: Vec<String> = Vec::new();
    let mut not_rolled_back: Vec<String> = Vec::new();

    for name in replaced.iter().rev() {
        let dump = crate::backup::archive::dump_database::dump_path(rollback, name);
        if replace_database(host, name, &dump).is_ok() {
            rolled_back.push((*name).as_str().to_owned());
        } else {
            not_rolled_back.push((*name).as_str().to_owned());
        }
    }

    if not_rolled_back.is_empty() {
        BackupError::RolledBack {
            failed: failed.as_str().to_owned(),
            rolled_back,
        }
    } else {
        BackupError::RolledBackPartially {
            failed: failed.as_str().to_owned(),
            rolled_back,
            not_rolled_back,
        }
    }
}

/// Step 7: the two renames.
///
/// The order is fixed and each rename is a different kind of risk. The home is
/// moved ASIDE first and the staging tree moved in second, so that the moment
/// of danger is a window between two renames on one filesystem rather than a
/// destructive delete. `rename` on one filesystem is a directory-entry
/// operation: it does not copy the customer's twenty gigabytes, and it either
/// happened or it did not.
///
/// Reversed — staging in first, then the old home aside — there is no window at
/// all, because the first rename would have to overwrite a non-empty directory,
/// which `rename` refuses, and the restore could never complete. The order is
/// the only one that works, and the window it costs is measured in
/// microseconds.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] when the home cannot be moved aside at all
/// — at that point nothing has changed — and [`BackupError::HomeParkedAt`] when
/// the second rename failed AND reversing the first failed too, carrying the
/// path an operator has to move it back from.
fn swap_home(
    placement: &Placement,
    account: &AccountName,
    sink: &mut dyn RestoreSink,
) -> Result<(), BackupError> {
    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.start_percent(),
    );

    let home = placement.home_root.join(account.as_str());
    rename(&home, &placement.previous).map_err(|_| BackupError::StagingUnusable)?;

    // The one report that falls BETWEEN the two renames, and the only point in
    // the whole operation at which the account has no home. It is reported for
    // the reason every other stage boundary is: a fixed span is what makes a
    // stall legible, and an operation stuck at the start of `restoring_files`
    // and one stuck between the renames are two very different states for an
    // operator to walk into — the second one has a home parked under
    // `RESTORE_STAGING_ROOT`. `percent_through` rather than a literal, so no
    // call site writes a percentage down (see `RestoreStage`).
    //
    // It is also the seam the polygon suite kills the process through, which is
    // stated rather than left for a reader to discover: a test that has to
    // interrupt this window needs a real moment inside it, and this is the
    // agent's own report rather than a hook added for a test.
    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.percent_through(1, 2),
    );

    if rename(&placement.staging, &home).is_err() {
        // Immediately, and before anything else is attempted: every microsecond
        // here is a microsecond in which the account has no home.
        return match rename(&placement.previous, &home) {
            Ok(()) => Err(BackupError::StagingUnusable),
            Err(_) => Err(BackupError::HomeParkedAt {
                path: placement.previous.to_string_lossy().into_owned(),
            }),
        };
    }

    sink.report(
        RestoreStage::RestoringFiles,
        RestoreStage::RestoringFiles.end_percent(),
    );
    Ok(())
}

/// Step 8: the home root's ownership and mode, and the parked tree.
///
/// **Measured, not assumed.** An account's home is created owned by the account
/// but group-owned by the WEB SERVER's group, mode `0750`, because the web
/// server has to traverse the home to serve the site. A restore that put the
/// account's own group back would break every site the account owns —
/// silently, with a 403 from nginx and no error anywhere in this operation. So
/// the group is re-applied here from the value the caller resolved out of the
/// distro adapter, and it is re-applied BEFORE the parked tree is removed, so
/// that a failure at this step is one an operator can still undo by hand.
///
/// Only the home ROOT is touched. Everything inside it was written by the
/// account, by construction, because the extraction ran as the account.
///
/// # Errors
///
/// [`BackupError::StagingUnusable`] when the ownership or mode cannot be
/// applied. The removal of the parked tree is NOT an error: at this point the
/// restore has succeeded, and a leftover directory is disk space to reclaim,
/// not a customer who cannot work.
fn finalise(
    placement: &Placement,
    account: &AccountName,
    sink: &mut dyn RestoreSink,
) -> Result<(), BackupError> {
    sink.report(
        RestoreStage::Finalising,
        RestoreStage::Finalising.start_percent(),
    );

    let home = placement.home_root.join(account.as_str());
    chown(&home, Some(placement.owner), Some(placement.group))
        .map_err(|_| BackupError::StagingUnusable)?;
    std::fs::set_permissions(&home, std::fs::Permissions::from_mode(HOME_MODE))
        .map_err(|_| BackupError::StagingUnusable)?;

    let _ = remove_dir_all(&placement.previous);

    sink.report(
        RestoreStage::Finalising,
        RestoreStage::Finalising.end_percent(),
    );
    Ok(())
}

/// Refuses an archive whose sidecar and internal manifest do not say the same
/// thing.
///
/// Two copies of one document exist on purpose: a listing reads the sidecar
/// without decompressing gigabytes, and a restore reads the copy inside the
/// archive. Comparing them is what makes editing the cheap copy useless — a
/// sidecar claiming a smaller database list, or another digest, is refused
/// rather than believed.
///
/// A sidecar that cannot be read or parsed is refused too, and not treated as
/// "no second opinion available". A check that reports "safe" when it could not
/// observe anything is the check this repository has already been burned by.
/// That refusal is now structural as well as written down: `read_sidecar`
/// answers a `ReadableBackup`, whose manifest and digest are not optional, so
/// there is no half-empty value for this comparison to be made against.
///
/// Read through [`read_sidecar`], the same function
/// [`list_backups`](crate::backup::list_backups) uses: a sidecar naming a
/// version this agent does not understand is refused right there, before its
/// `manifest` or `artifact_sha256` field is ever compared to anything. That
/// used not to be true — this function used to parse the sidecar on its own
/// and never look at its version at all, so a listing could refuse an
/// artifact as unreadable while a restore accepted the very same sidecar.
/// Going through the one shared reader closes that gap here and for whatever
/// third caller reads a sidecar next.
///
/// # Errors
///
/// [`BackupError::ManifestDisagreesWithSidecar`], including when the sidecar
/// names a version this agent does not understand.
fn require_sidecar_agrees(
    sidecar: &Path,
    manifest: &BackupManifest,
    artifact_sha256: &str,
) -> Result<(), BackupError> {
    // A sidecar that describes nothing is a disagreement, not an absence of
    // one — this check reports on what it could observe, and a sidecar it
    // could not read is no second opinion at all. `read_sidecar` answers a
    // `ReadableBackup`, so that case cannot arrive here as a value with
    // nothing in it: it arrives as the `Err` this line refuses.
    let details =
        read_sidecar(sidecar).map_err(|_reason| BackupError::ManifestDisagreesWithSidecar)?;

    let agrees =
        &details.manifest == manifest && details.artifact_sha256.as_str() == artifact_sha256;
    if !agrees {
        return Err(BackupError::ManifestDisagreesWithSidecar);
    }

    Ok(())
}

/// Pairs every database the manifest names with the validated name the panel
/// still knows it by.
///
/// **`allowed_databases` is the panel's decision, carried on the request, and a
/// manifest entry that is not in it is REFUSED — never created.** The panel is
/// the only thing that knows which databases still exist as far as the product
/// is concerned; the archive knows only what was true when it was written.
/// Creating a database the panel has forgotten would resurrect it on the server
/// with no row anywhere saying who owns it or who may reach it — an orphan
/// owned by a user nothing points at, invisible to every screen and to every
/// later cleanup, holding a customer's data.
///
/// Refusing the whole restore rather than skipping the entry is the same
/// judgement the member pre-scan makes: an archive whose contents this panel
/// cannot account for is one it does not understand, and quietly restoring the
/// part it recognised is how a customer is told a restore succeeded when a
/// database is missing from it.
///
/// The pairing also does the work of validation: the returned name is a
/// [`DatabaseName`] the panel supplied, so nothing that reaches the dump
/// client's argv or a dump file's path came out of the archive.
///
/// # Errors
///
/// [`BackupError::UnknownDatabase`], naming the first entry the panel does not
/// know. The name is escaped on the way in, because a manifest is archive
/// content like any other member.
fn resolve_databases(
    manifest: &BackupManifest,
    allowed: &[DatabaseName],
) -> Result<Vec<(DatabaseName, ManifestDatabase)>, BackupError> {
    let mut resolved = Vec::with_capacity(manifest.databases.len());
    for entry in &manifest.databases {
        let known = allowed
            .iter()
            .find(|name| name.as_str() == entry.name)
            .ok_or_else(|| BackupError::UnknownDatabase {
                name: refused_member_name(&entry.name),
            })?;
        resolved.push((known.clone(), entry.clone()));
    }

    Ok(resolved)
}

/// Creates the root-only scratch this restore stages into, and answers it —
/// after proving, against the inodes, that the place it is about to create it
/// in is root's alone all the way up.
///
/// Anything left by a crashed earlier run under this id is removed first: the
/// id is this operation's own, so what is there can only be its own litter —
/// and a stale dump left in place would be loaded into a live database as if
/// this run had extracted it.
///
/// # Why the chain is checked and not just the leaf
///
/// The same argument [`create_backup`](crate::backup::create_backup) makes for
/// its own scratch, and it is if anything sharper here: a restore extracts the
/// archive's `databases/*` into this directory as ROOT and then loads them, so
/// an ancestor somebody else owns buys them both a plaintext copy of every
/// database in the backup and a say in what gets loaded back into the live
/// one. The two operations stage under the same root, so a boundary enforced
/// by one of them and not the other is not a boundary — the check itself lives
/// in [`root_only_chain`](crate::backup::root_only_chain) so there is exactly
/// one of it.
///
/// # Errors
///
/// [`BackupError::ScratchUnusable`] — for a chain that could not be created,
/// and for one this agent refuses to write into. The two are one variant
/// because the answer to both is the same: nothing was staged, and the restore
/// stops before the archive has been opened.
///
/// # Why the base, the root and the owner are parameters
///
/// So the accepting path can be exercised: a test does not run as uid 0 and
/// cannot create a directory owned by root, and a gate that has only ever been
/// handed input it must reject passes just as well once it has been mutated
/// into rejecting everything (rules/testing.md, "a refusing gate needs an
/// inverse control"). [`restore_backup`] fills all three from [`AgentPaths`]
/// and the account's own uid; nothing in them comes from a request.
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

    DirBuilder::new()
        .recursive(true)
        .mode(SCRATCH_MODE)
        .create(scratch)
        .map_err(|_| BackupError::ScratchUnusable)?;

    // And again on the finished chain: a TOCTOU re-assertion, and nothing else.
    //
    // Said precisely, because the reason written here before was measured to be
    // wrong. It claimed this arm catches a directory this call did not create —
    // one left by an older version, or chmodded since. It does not: such a
    // directory is already refused by the arm above, and what this call itself
    // created cannot be looser than `SCRATCH_MODE`, because a umask can only
    // take bits away. Every state this arm can refuse that the pre-arm cannot
    // is a state produced INSIDE the window between the two — a level renamed
    // aside and replaced between the check above and the `create` below.
    //
    // That window is a race, so NO TEST OBSERVES THIS ARM, in this repository
    // or in principle in-process: a mutation that deletes it survives the whole
    // suite (ledger S9). It is kept rather than deleted because rules/testing.md
    // deletes a defensive call that CANNOT FAIL, and this one can — what cannot
    // happen is the test. It is held by review, and a coverage report that
    // shows it unreached is reporting the truth rather than a gap to close.
    require_root_only_chain(base, root, scratch, owner, MissingLevels::Refused)?;

    Ok(scratch.to_path_buf())
}

#[cfg(test)]
#[path = "../tests/backup/restore_backup_tests.rs"]
mod tests;
