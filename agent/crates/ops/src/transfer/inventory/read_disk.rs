//! The last step of an inventory read: the size of each home and its mail.

use super::inventory_run::InventoryRun;
use super::model::account_draft::AccountDraft;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::parse_home_usage::parse_home_usage;
use super::parse_mailbox_count::parse_mailbox_count;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Measures each account's home and counts the mailboxes under it.
///
/// A size is taken only from a command that exited `0` and printed a figure; any
/// other answer leaves the size "not measured" and sends a `DiskUnreadable`
/// naming the account (a measurement that failed part-way is not a size). The
/// mailbox count follows the same rule, and a count above zero sends
/// `MailNotMigrated` naming the account, because mail is found and not moved.
///
/// # Errors
///
/// Every connection error the shell returns.
pub fn read_disk(
    run: &mut InventoryRun<'_>,
    drafts: &mut [AccountDraft],
) -> Result<(), TransferOpError> {
    for draft in drafts.iter_mut() {
        let unreadable = SourceWarningRecord::about_account(
            SourceWarningKind::DiskUnreadable,
            &draft.entry.name,
        );

        let usage = run.outcome(&RemoteCommand::HomeDiskUsage {
            home: draft.entry.home.clone(),
        })?;
        match usage
            .filter(|outcome| outcome.status == 0)
            .and_then(|outcome| parse_home_usage(&outcome.stdout))
        {
            Some(bytes) => draft.disk_bytes = Some(bytes),
            None => run.warn(unreadable.clone()),
        }

        let mailboxes = run.outcome(&RemoteCommand::CountMailboxes {
            home: draft.entry.home.clone(),
        })?;
        match mailboxes
            .filter(|outcome| outcome.status == 0)
            .and_then(|outcome| parse_mailbox_count(&outcome.stdout))
        {
            Some(count) => {
                draft.mailbox_count = count;
                if count > 0 {
                    run.warn(SourceWarningRecord::about_account(
                        SourceWarningKind::MailNotMigrated,
                        &draft.entry.name,
                    ));
                }
            }
            None => run.warn(unreadable),
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_disk_tests.rs"]
mod tests;
