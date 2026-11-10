//! The fourth step of an inventory read: the crontabs.

use maran_agent_core::validation::system::source_user_name::SourceUserName;

use super::inventory_run::InventoryRun;
use super::model::account_draft::AccountDraft;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::parse_crontab::parse_crontab;
use crate::transfer::{RemoteCommand, TransferOpError};

/// The exit status `crontab -l` ends with for a user who has no crontab. The
/// remote command discards the tool's own words, so this status is all that is
/// left to tell "no crontab" from a tool that did not run; it is also the status
/// of a refusal to read another user's crontab, which the report cannot tell
/// apart and does not claim to.
const NO_CRONTAB_STATUS: i32 = 1;

/// Counts each account's crontab entries.
///
/// Per account: a status of `0` is read with `parse_crontab`; status `1` is "no
/// crontab" and counts zero without a warning; any other status is a
/// `CronUnreadable` warning naming the account. Lines `parse_crontab` could not
/// tell from noise are one `CronUnreadable`, and a bound it hit is one
/// `Truncated`. An account name that is not a valid remote login (it always is,
/// but the type is asked for) is a `CronUnreadable` and no command is run.
///
/// # Errors
///
/// Every connection error the shell returns.
pub fn read_cron(
    run: &mut InventoryRun<'_>,
    drafts: &mut [AccountDraft],
) -> Result<(), TransferOpError> {
    for draft in drafts.iter_mut() {
        let unreadable = SourceWarningRecord::about_account(
            SourceWarningKind::CronUnreadable,
            &draft.entry.name,
        );
        let Ok(user) = SourceUserName::parse(draft.entry.name.as_str()) else {
            run.warn(unreadable);
            continue;
        };
        let Some(outcome) = run.outcome(&RemoteCommand::ReadCrontab { user })? else {
            continue;
        };
        match outcome.status {
            0 => {
                let reading = parse_crontab(&outcome.stdout);
                draft.cron_entry_count = reading.entry_count;
                if reading.skipped_lines > 0 {
                    run.warn(unreadable);
                }
                if reading.truncated {
                    run.warn(SourceWarningRecord::about_nothing(
                        SourceWarningKind::Truncated,
                    ));
                }
            }
            NO_CRONTAB_STATUS => {}
            _ => run.warn(unreadable),
        }
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_cron_tests.rs"]
mod tests;
