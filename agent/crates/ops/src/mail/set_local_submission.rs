//! Binding one account's local mail submission to its own senders and limit.

use maran_agent_core::validation::mail::local_submission::LocalSubmission;
use maran_distro::DistroAdapter;

use crate::mail::local_submission_guard::assert_drop_road_sealed;
use crate::mail::local_submission_tables::{AccountLines, apply_account_lines};
use crate::mail::mail_error::MailError;
use crate::mail::mail_host::MailHost;

/// Replaces everything the account has in the local-submission tables with
/// `submission`, as one unit (contract `docs/mail/local-submission.md`, issue #518).
///
/// The account's old lines are dropped from all six tables and its new ones put
/// in, so a domain or mailbox the panel no longer lists disappears. Other accounts'
/// lines are untouched, except that a uid is held by ONE account (the newest
/// binding wins) and a sender domain or mailbox login owned by another account is
/// refused. The whole read-render-write runs under one lock. The Rspamd maps are
/// written first and Rspamd reloaded and shown to report the rule, then Postfix's
/// `maran_local_submit_users` is written and indexed: the account is never
/// admitted by `postdrop` before the filter knows its rules.
///
/// The host invariants the rule stands on are checked first (see
/// `assert_drop_road_sealed`).
///
/// # Errors
///
/// Returns [`MailError::UnsafeLocalSubmission`] before anything is written when the
/// host would let the system user bypass the rule (it is in the `postdrop` or
/// Postfix group, it is not in the user database, or `postdrop` is not mode 2755
/// root), [`MailError::LocalSubmissionConflict`] when a domain or login belongs to
/// another account (nothing written), [`MailError::UnsafeLocalSubmission`] with
/// `RuleNotLoaded` when Rspamd does not report the rule after the reload (Postfix
/// is not told to admit the account), [`MailError::ConfigRejected`] when a validator refuses (the files of
/// that change are restored), [`MailError::ServiceRefused`] when the Rspamd reload
/// is refused, [`MailError::ConfigUnreadable`] when a table exists and cannot be
/// read, and [`MailError::SpawnFailed`], [`MailError::Bound`] and
/// [`MailError::ConfigWrite`] for the mechanical failures.
pub fn set_local_submission(
    host: &dyn MailHost,
    distro: &dyn DistroAdapter,
    submission: &LocalSubmission,
) -> Result<(), MailError> {
    assert_drop_road_sealed(host, distro, submission.uid)?;
    let account = submission.account.as_str();
    let lines = AccountLines {
        local_accounts: vec![format!("{} {account}", submission.uid)],
        sender_domains: submission
            .domains
            .iter()
            .map(|domain| format!("{account} {}", domain.as_str()))
            .collect(),
        send_limits: vec![format!(
            "{account} {}",
            submission.send_limit_per_hour.value()
        )],
        mailbox_accounts: submission
            .mailbox_logins
            .iter()
            .map(|login| format!("{} {account}", login.to_address()))
            .collect(),
        submit_users: vec![format!("{account} OK")],
        alias_fanouts: submission
            .alias_fanouts
            .iter()
            .map(|fanout| {
                format!(
                    "{} {} {account}",
                    fanout.recipient.to_key(),
                    fanout.external_targets
                )
            })
            .collect(),
    };
    apply_account_lines(host, distro, account, &lines, true)
}

#[cfg(test)]
#[path = "../tests/mail/set_local_submission_tests.rs"]
mod tests;
