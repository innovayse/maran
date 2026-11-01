//! Step 6 of a restore: each database in turn — rollback dump, drop, load.

use std::fs::DirBuilder;
use std::os::unix::fs::DirBuilderExt as _;
use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;

use crate::backup::archive::dump_database::dump_database;
use crate::backup::archive::extract_databases_as_root::extracted_dump_path;
use crate::backup::archive::replace_database::replace_database;
use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::model::restore_stage::RestoreStage;
use crate::backup::require_scratch_room::require_scratch_room;
use crate::backup::restore_placement::RestorePlacement;
use crate::backup::restore_plan::RestorePlan;
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
/// `require_scratch_room`, applied before the first database is dropped.
pub(super) const MAXIMUM_DUMP_BYTES: u64 = 8 * 1024 * 1024 * 1024;

/// The scratch's directory of pre-drop rollback dumps.
///
/// Beside `databases/` rather than inside it, so that a rollback dump can never
/// be mistaken for one the archive supplied — they are dumps of opposite
/// provenance and only one of them has a manifest digest.
pub(super) const ROLLBACK_DIRECTORY: &str = "rollback";

/// Step 6: each database in turn — rollback dump, drop, load.
///
/// Answers how many were replaced, which on this function's success is always
/// every one of them: a partial pass returns an error, never a smaller number.
/// That is R7 made structural. A restore that "restored four of five" and
/// reported success is the account-deletion cascade's defect with a different
/// noun, and the way to make it unreportable is to have no success value that
/// can describe it — the count `RestoreOutcome` carries is the count this
/// function returns, and this function returns only `total`.
///
/// # Errors
///
/// [`BackupError::RolledBack`] or [`BackupError::RolledBackPartially`] for a
/// failure at or after the first drop, and the dump client's own errors for a
/// failure taking the very first rollback dump — which happens before any drop
/// and therefore leaves everything untouched.
pub(super) fn replace_databases(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    planned: &RestorePlan,
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
/// This function used to fill a `RestoreOutcome` and then return a
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
