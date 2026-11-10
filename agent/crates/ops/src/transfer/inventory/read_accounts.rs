//! The first step of an inventory read: the accounts.

use super::inventory_run::InventoryRun;
use super::model::account_draft::AccountDraft;
use super::model::bounded_text::BoundedText;
use super::model::source_warning_kind::SourceWarningKind;
use super::model::source_warning_record::SourceWarningRecord;
use super::parse_passwd::parse_passwd;
use crate::transfer::{RemoteCommand, TransferOpError};

/// Lists the source's accounts and starts one draft per hosting account.
///
/// The account list is the one thing the rest of the read hangs on, so unlike
/// every later step its failure ends the read: a listing that cannot be run, or
/// exits non-zero, or was cut off by the command bound, is not an inventory.
///
/// # Errors
///
/// - [`TransferOpError::InvalidRemoteOutput`] when the listing gave no usable
///   answer.
/// - Every connection error the shell returns.
pub fn read_accounts(run: &mut InventoryRun<'_>) -> Result<Vec<AccountDraft>, TransferOpError> {
    let outcome = run
        .outcome(&RemoteCommand::ListPasswd)?
        .filter(|outcome| outcome.status == 0)
        .ok_or(TransferOpError::InvalidRemoteOutput)?;
    let listing = BoundedText::of(outcome.stdout);
    if listing.truncated {
        run.warn(SourceWarningRecord::about_nothing(
            SourceWarningKind::Truncated,
        ));
    }
    let parsed = parse_passwd(&listing.text);
    run.warn_each(parsed.warnings);
    Ok(parsed.items.into_iter().map(AccountDraft::new).collect())
}

#[cfg(test)]
#[path = "../../tests/transfer/inventory/read_accounts_tests.rs"]
mod tests;
