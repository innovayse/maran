//! Running one file-manager job as the account and reading its answer back.

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::child_refusal::ChildRefusal;
use maran_agent_core::privs::child_tally::ChildTally;
use maran_agent_core::privs::fork_reporting_as_account::fork_reporting_as_account;

use crate::files::FilesOpError;
use crate::files::files_error_of_privilege::files_error_of_privilege;
use crate::files::files_refusal::FilesRefusal;
use crate::files::work_patience::WorkPatience;

/// Runs `work` in a forked child that has dropped to `ids` and answers its two figures.
///
/// The one place the file manager's mutations touch the reporting fork. The child
/// hands back exactly what the fork's report channel carries: two integers on
/// success, or one [`FilesRefusal`] byte; nothing else crosses, no path and no
/// text. The parent decodes integers only and reads a byte outside the list as a
/// plain failure, so a confused or compromised child cannot choose what error the
/// panel is told beyond that closed set.
///
/// The two figures mean whatever the caller's operation says (entries and bytes,
/// changed and skipped, a count and nothing); a caller names them at once, and the
/// positions are never passed further than the host method that read them.
/// `work` must return an error for every failure it knows and never panic: a
/// child that dies is reported as the fork reports it, not as a refusal.
///
/// Callers MUST invoke this from `tokio::task::spawn_blocking`, as every fork.
///
/// # Errors
///
/// Returns [`FilesOpError::Privilege`] when the fork, the drop, the wait or the
/// report fails, and otherwise whatever the child refused with.
pub(crate) fn fork_files_work<F>(
    ids: &AccountIds,
    patience: WorkPatience,
    work: F,
) -> Result<(u64, u64), FilesOpError>
where
    F: FnOnce() -> Result<(u64, u64), FilesOpError>,
{
    fork_reporting_as_account(ids, patience.duration(), None, |_context| match work() {
        Ok((first, second)) => Ok(ChildTally::new(first, second)),
        Err(error) => Err(ChildRefusal::new(FilesRefusal::of(&error).code())),
    })
    .map(|report| (report.first, report.second))
    .map_err(files_error_of_privilege)
}
