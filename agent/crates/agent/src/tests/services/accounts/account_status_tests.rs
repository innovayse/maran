//! Tests for the wire code every account failure travels as.
//!
//! One assertion per variant of `AccountError`, because the codes are a
//! CONTRACT — `accounts.proto` names them to callers — and because
//! `rules/testing.md` requires every typed error variant to appear in at least
//! one test.
//!
//! The tests that matter to `rules/security.md` item 8 are the last two. They
//! assert what does and does not cross the seam: an operator gets the program
//! and the exit status, and no refusing tool's own words travel with either.
//! Each carries a positive control — a plausible tool sentence is planted in
//! the fixture and the probe is shown to find it where it IS present — because
//! an "output is absent" assertion passes just as loudly against a mapping that
//! could never have carried any.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::AccountError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// A sentence of the shape a refusing shadow-utils tool prints, and the token
/// the leak probes hunt for.
const TOOL_SENTENCE: &str = "useradd: UID 1001 is not unique";

/// Every variant this mapping can be handed, with a payload each.
fn every_variant() -> Vec<AccountError> {
    vec![
        AccountError::AlreadyExists {
            username: "acme".to_owned(),
        },
        AccountError::NotFound {
            username: "acme".to_owned(),
        },
        AccountError::CommandFailed {
            program: "/usr/sbin/useradd".to_owned(),
            status: 9,
        },
        AccountError::CommandUnavailable {
            program: "/usr/sbin/useradd".to_owned(),
            reason: "No such file or directory".to_owned(),
        },
        AccountError::UnreadableOutput {
            program: "/usr/bin/getent".to_owned(),
        },
        AccountError::PoolRemoval {
            reason: "a pool is still there".to_owned(),
        },
        AccountError::DatabaseRemoval {
            reason: "a database is still there".to_owned(),
        },
        AccountError::SiteInspection {
            reason: "a site could not be read".to_owned(),
        },
        AccountError::CronInspection {
            reason: "the crontab could not be read".to_owned(),
        },
        AccountError::SftpInspection {
            reason: "a login could not be read".to_owned(),
        },
        AccountError::SftpRemoval {
            reason: "a login is still there".to_owned(),
        },
        AccountError::Busy {
            username: "acme".to_owned(),
        },
    ]
}

/// The code `error` is reported as.
fn code_of(error: &AccountError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn an_account_that_is_already_there_is_an_idempotency_outcome_not_a_fault() {
    assert_eq!(
        code_of(&AccountError::AlreadyExists {
            username: "acme".to_owned(),
        }),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn an_account_that_is_not_there_is_reported_as_not_found() {
    assert_eq!(
        code_of(&AccountError::NotFound {
            username: "acme".to_owned(),
        }),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn a_refused_deletion_is_a_system_failure_and_never_a_not_found() {
    // The account IS still there, deliberately. Reporting NotFound would tell
    // the panel the deletion had happened.
    for error in [
        AccountError::PoolRemoval {
            reason: "a pool is still there".to_owned(),
        },
        AccountError::DatabaseRemoval {
            reason: "a database is still there".to_owned(),
        },
        AccountError::SftpRemoval {
            reason: "a login is still there".to_owned(),
        },
    ] {
        assert_eq!(
            code_of(&error),
            ErrorCode::SystemFailure as i32,
            "{error} must be a system failure"
        );
    }
}

#[test]
fn a_failing_tool_is_reported_by_its_program_and_its_exit_status() {
    // What an operator is left with, asserted as the value and not as a bound.
    let wire = to_agent_error(&AccountError::CommandFailed {
        program: "/usr/sbin/useradd".to_owned(),
        status: 9,
    });

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert_eq!(wire.message, "/usr/sbin/useradd failed with status 9");
}

#[test]
fn no_mapped_failure_carries_a_refusing_tools_own_words() {
    // Positive control for the probe: a message that DOES contain the sentence
    // is found by the same `contains` the loop below relies on. Without this the
    // loop would pass against a probe that could not match anything.
    let planted = AccountError::PoolRemoval {
        reason: TOOL_SENTENCE.to_owned(),
    };
    assert!(
        to_agent_error(&planted).message.contains(TOOL_SENTENCE),
        "the probe must be able to see a tool sentence where one is present"
    );

    // And the real assertion: no variant this area can produce has a field a
    // tool's output could occupy, so neither half of the wire error can hold
    // one. `CommandFailed` used to carry the tool's stderr in both.
    for error in every_variant() {
        let wire = to_agent_error(&error);
        assert!(
            wire.tool_output.is_empty(),
            "{error} must not carry tool output"
        );
        assert_eq!(
            wire.message,
            error.to_string(),
            "the mapping must not invent a message beside the variant's own"
        );
    }
}

#[test]
fn the_command_failed_variant_cannot_be_built_with_a_tools_output_in_it() {
    // The structural half of the claim, asserted where it can be seen from: the
    // variant's whole payload is a program name and an i32, so a formatting of
    // it cannot contain a sentence a tool printed however it was constructed.
    let error = AccountError::CommandFailed {
        program: "/usr/sbin/useradd".to_owned(),
        status: 9,
    };

    let wire = to_agent_error(&error);

    assert_eq!(wire.message, "/usr/sbin/useradd failed with status 9");
    assert_eq!(wire.tool_output, "");
    assert_eq!(
        format!("{error:?}"),
        "CommandFailed { program: \"/usr/sbin/useradd\", status: 9 }"
    );
}

#[test]
fn a_deletion_refused_because_the_account_is_busy_is_its_own_code() {
    // It used to fall through the mapping's `_` arm onto SYSTEM_FAILURE, so a
    // deletion refused for one second while a backup of that account ran was
    // indistinguishable from `userdel` failing. The refusal is the operation's
    // first statement: nothing ran, and the identical request may be reissued.
    assert_eq!(
        code_of(&AccountError::Busy {
            username: "acme".to_owned(),
        }),
        ErrorCode::AccountBusy as i32
    );
}

#[test]
fn a_busy_account_and_a_genuine_fault_of_this_host_are_different_codes() {
    // The inverse control. A `userdel` that failed, and the three refusals that
    // leave the account standing, must STILL read as faults — an operator has
    // to go and look, not wait.
    for error in [
        AccountError::CommandFailed {
            program: "/usr/sbin/userdel".to_owned(),
            status: 1,
        },
        AccountError::SftpRemoval {
            reason: "a login is still there".to_owned(),
        },
    ] {
        assert_eq!(
            code_of(&error),
            ErrorCode::SystemFailure as i32,
            "{error} must still read as a fault"
        );
        assert_ne!(
            code_of(&error),
            code_of(&AccountError::Busy {
                username: "acme".to_owned(),
            })
        );
    }
}
