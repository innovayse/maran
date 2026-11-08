//! Running one upload as the account: the parent feeds, the child writes, the child answers.

use std::io;

use maran_agent_core::privs::account_ids::AccountIds;
use maran_agent_core::privs::child_context::ChildContext;
use maran_agent_core::privs::child_input_writer::ChildInputWriter;
use maran_agent_core::privs::child_refusal::ChildRefusal;
use maran_agent_core::privs::child_tally::ChildTally;
use maran_agent_core::privs::fork_reporting_as_account::fork_reporting_as_account;

use crate::files::FilesOpError;
use crate::files::files_error_of_privilege::files_error_of_privilege;
use crate::files::files_refusal::FilesRefusal;
use crate::files::work_patience::WorkPatience;

/// Runs `work` in a forked child that has dropped to `ids`, while `feed` sends it the upload,
/// and answers the one figure it reports: the bytes written.
///
/// The twin of `fork_files_work` for the one operation that has an input: it differs in the
/// feed and in nothing else, including how a refusal number crosses back, which is the same
/// closed list read the same way. A number outside the list is a plain failure, so a confused
/// child cannot choose what the panel is told beyond it.
///
/// Callers MUST invoke this from `tokio::task::spawn_blocking`; `feed` runs on that thread.
///
/// # Errors
///
/// [`FilesOpError::Privilege`] when the fork, the drop, the wait, the report or the channel
/// fails, and otherwise whatever the child refused with.
pub(crate) fn fork_upload_work<F>(
    ids: &AccountIds,
    feed: &mut dyn FnMut(&mut ChildInputWriter<'_>) -> io::Result<()>,
    work: F,
) -> Result<u64, FilesOpError>
where
    F: FnOnce(&mut ChildContext<'_>) -> Result<u64, FilesOpError>,
{
    fork_reporting_as_account(
        ids,
        WorkPatience::Bulk.duration(),
        Some(feed),
        |context| match work(context) {
            Ok(written) => Ok(ChildTally::new(written, 0)),
            Err(error) => Err(ChildRefusal::new(FilesRefusal::of(&error).code())),
        },
    )
    .map(|report| report.first)
    .map_err(files_error_of_privilege)
}
