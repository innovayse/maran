//! What a privilege failure is reported as, and what it must never be.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::privs::priv_error::PrivError;
use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;

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
        PrivError::DropFailed { syscall: "setuid" },
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
    let broken = CronError::Privilege(PrivError::DropFailed { syscall: "setuid" }).to_string();

    assert_ne!(refused, broken);
    assert!(refused.contains(&PrivError::RootAccount.to_string()));
    assert!(broken.contains(&PrivError::DropFailed { syscall: "setuid" }.to_string()));
}

/// An account refusal says the same thing whichever check failed.
#[test]
fn an_account_refusal_reads_the_same_whichever_check_failed() {
    // Naming which check failed would describe the shape of the rules to
    // whoever is probing them; the reason is the operator log's, by variant.
    let messages: Vec<String> = [
        ManagedAccountError::Reserved,
        ManagedAccountError::NotFound,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ]
    .into_iter()
    .map(|reason| CronError::Account(reason).to_string())
    .collect();

    assert!(
        messages.windows(2).all(|pair| pair[0] == pair[1]),
        "the four refusals must read alike: {messages:?}"
    );
    assert!(
        !messages[0].contains("/home") && !messages[0].contains("reserved"),
        "the message names no rule and no path: {}",
        messages[0]
    );
}

/// An account refusal is not a privilege failure and not a crontab failure.
#[test]
fn an_account_refusal_is_its_own_variant() {
    let refusal = CronError::Account(ManagedAccountError::Reserved);

    assert!(matches!(refusal, CronError::Account(_)));
    assert_ne!(refusal, CronError::Privilege(PrivError::RootAccount));
    assert_ne!(refusal, CronError::CrontabRefused { code: 1 });
}
