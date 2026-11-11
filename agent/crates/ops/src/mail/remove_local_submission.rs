//! Taking an account out of local mail submission.

use maran_distro::DistroAdapter;

use crate::mail::local_submission_tables::{AccountLines, apply_account_lines};
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;

/// Removes every line `account` has in the local-submission tables (issue #518).
///
/// Postfix's table is rewritten first, so `postdrop` stops admitting the account
/// before the filter forgets it. Removing an account that has no line is a
/// success that rewrites the same bytes.
///
/// This removes the account's lines from all five tables, including the uid to
/// account line, so a system uid reused after the account is deleted starts
/// unbound. Messages the account already left in the maildrop queue are not
/// touched here: they are evaluated against the tables as they stand when
/// `pickup` reads them, so a removed account's backlog is refused, and the
/// account-deletion operation purges the account's maildrop files (contract
/// `docs/mail/local-submission.md`).
///
/// # Errors
///
/// As [`crate::mail::set_local_submission`].
pub fn remove_local_submission(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    account: &str,
) -> Result<(), MailError> {
    apply_account_lines(host, distro, account, &AccountLines::default(), false)
}

#[cfg(test)]
#[path = "../tests/mail/remove_local_submission_tests.rs"]
mod tests;
