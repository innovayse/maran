//! The third step of an inventory read: the databases.

use super::assign_databases_to_accounts::assign_databases_to_accounts;
use super::inventory_run::InventoryRun;
use super::model::account_draft::AccountDraft;
use super::model::passwd_entry::PasswdEntry;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::parse_mysql_listing::parse_mysql_listing;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Lists the source's databases and attributes each to the account whose prefix
/// its name carries.
///
/// A listing that cannot be read (no database server, access refused) is one
/// `DatabasesUnreadable` warning and every account keeps no databases. A database
/// that belongs to no account is one `NameRefused` warning without text.
///
/// # Errors
///
/// Every connection error the shell returns.
pub fn read_databases(
    run: &mut InventoryRun<'_>,
    drafts: &mut [AccountDraft],
) -> Result<(), TransferOpError> {
    let Some(listing) = run.text(
        &RemoteCommand::ListDatabases,
        SourceWarningRecord::about_nothing(SourceWarningKind::DatabasesUnreadable),
    )?
    else {
        return Ok(());
    };
    let parsed = parse_mysql_listing(&listing.text);
    run.warn_each(parsed.warnings);

    let entries: Vec<&PasswdEntry> = drafts.iter().map(|draft| &draft.entry).collect();
    let assignment = assign_databases_to_accounts(parsed.items, &entries);
    for (draft, databases) in drafts.iter_mut().zip(assignment.per_account) {
        draft.databases = databases;
    }
    for _ in 0..assignment.refused {
        run.warn(SourceWarningRecord::about_nothing(
            SourceWarningKind::NameRefused,
        ));
    }
    Ok(())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_databases_tests.rs"]
mod tests;
