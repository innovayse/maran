//! The irreversible half of a single-database restore.

use crate::backup::backup_error::BackupError;
use crate::backup::backup_host::BackupHost;
use crate::backup::model::restore_outcome::RestoreOutcome;
use crate::backup::model::restore_sink::RestoreSink;
use crate::backup::replace_databases::replace_databases;
use crate::backup::restore_placement::RestorePlacement;
use crate::backup::restore_plan::RestorePlan;

/// Replaces the planned database and nothing else (issue #160).
///
/// The same step 6 the whole restore runs, over a list of one: the database's
/// rollback dump is taken on the line before its drop, the drop is the point of
/// no return, and a failed load puts the rollback dump back. No file is read,
/// swapped or re-owned, so there is no marker, no staging tree to swap and no
/// identity to re-ask — the home is not touched at all.
///
/// The outcome is stated as it is: no files restored, one database of one. A
/// caller that read `files_restored` as "the account is whole again" would be
/// wrong, which is why the wire documents the field as scoped.
///
/// # Errors
///
/// As [`replace_databases`]: [`BackupError::RolledBack`] or
/// [`BackupError::RolledBackPartially`] for a failure at or after the drop, and
/// the dump client's own errors for a failure taking the rollback dump, which
/// leaves the database untouched.
pub(super) fn restore_databases_only(
    placement: &RestorePlacement,
    host: &dyn BackupHost,
    planned: &RestorePlan,
    sink: &mut dyn RestoreSink,
) -> Result<RestoreOutcome, BackupError> {
    let total = u32::try_from(planned.databases.len()).unwrap_or(u32::MAX);
    let restored = replace_databases(placement, host, planned, sink)?;

    Ok(RestoreOutcome {
        files_restored: false,
        databases_restored: restored,
        databases_total: total,
    })
}
