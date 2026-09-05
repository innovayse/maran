//! What a privilege failure is reported as, and what it must never be.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;

use crate::cron::cron_error::CronError;

/// A failure of the privilege drop keeps its own variant and its own reason.
#[test]
fn a_privilege_failure_converts_into_the_privilege_variant() {
    // The `?` on `AccountIds::resolve` and on `fork_as_account` is what carries
    // these across, and this is the conversion it uses. Reported as anything
    // else, an account this host cannot resolve would read to an operator as a
    // file that would not write — sending them to look at a disk instead of at
    // the password database.
    let failure: CronError = PrivError::VerificationFailed.into();

    assert_eq!(failure, CronError::Privilege(PrivError::VerificationFailed));
}

/// A privilege failure is never reported as a file failure.
#[test]
fn a_privilege_failure_is_not_reported_as_a_file_failure() {
    for reason in [
        PrivError::NoSuchAccount,
        PrivError::RootAccount,
        PrivError::DropFailed,
        PrivError::VerificationFailed,
    ] {
        let failure: CronError = reason.into();

        assert!(
            matches!(failure, CronError::Privilege(_)),
            "a privilege failure must keep its own variant: {failure:?}"
        );
    }
}

/// The operator is told which part of the drop failed, not merely that one did.
#[test]
fn a_privilege_failure_carries_the_underlying_reason_in_its_message() {
    // `RootAccount` and `DropFailed` are opposite kinds of problem — a request
    // the agent refused, and a syscall that did not work — and an operator
    // reading one line has to be able to tell them apart.
    let refused = CronError::Privilege(PrivError::RootAccount).to_string();
    let broken = CronError::Privilege(PrivError::DropFailed).to_string();

    assert_ne!(refused, broken);
    assert!(refused.contains(&PrivError::RootAccount.to_string()));
    assert!(broken.contains(&PrivError::DropFailed.to_string()));
}
