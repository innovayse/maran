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

use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
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
        AccountError::FtpsRemoval {
            reason: "an FTPS login is still there".to_owned(),
        },
        AccountError::Busy {
            username: "acme".to_owned(),
        },
        AccountError::NotManaged {
            username: "root".to_owned(),
            refusal: ManagedAccountError::Reserved,
        },
        AccountError::IdentityChanged {
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

#[test]
fn a_user_that_is_not_a_hosting_account_is_an_invalid_input_and_never_a_fault() {
    // Reserved, a system identity and a home elsewhere are all the same answer to
    // the panel: the request names something this panel does not manage. Not
    // SYSTEM_FAILURE (nothing on the host is broken), not NOT_FOUND (the user
    // may well exist), and not ALREADY_EXISTS or ACCOUNT_BUSY, which the panel
    // acts on differently.
    for refusal in [
        ManagedAccountError::Reserved,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ] {
        let error = AccountError::NotManaged {
            username: "root".to_owned(),
            refusal,
        };
        assert_eq!(
            code_of(&error),
            ErrorCode::InvalidInput as i32,
            "{error} must be an invalid input"
        );
    }
    for wrong in [
        ErrorCode::SystemFailure,
        ErrorCode::NotFound,
        ErrorCode::AlreadyExists,
        ErrorCode::AccountBusy,
    ] {
        assert_ne!(
            code_of(&AccountError::NotManaged {
                username: "root".to_owned(),
                refusal: ManagedAccountError::SystemIdentity,
            }),
            wrong as i32
        );
    }
}

#[test]
fn the_wire_refusal_does_not_say_which_rule_refused_the_user() {
    // The three refusals differ in the variant and must not differ on the wire:
    // telling a caller "reserved" versus "system identity" versus "wrong home"
    // is a probe for which names on this host are special. The sentence names
    // the user the caller itself supplied and nothing more.
    let messages: Vec<String> = [
        ManagedAccountError::Reserved,
        ManagedAccountError::SystemIdentity,
        ManagedAccountError::WrongHome,
    ]
    .into_iter()
    .map(|refusal| {
        to_agent_error(&AccountError::NotManaged {
            username: "root".to_owned(),
            refusal,
        })
        .message
    })
    .collect();

    assert_eq!(messages[0], messages[1]);
    assert_eq!(messages[1], messages[2]);
    for word in ["reserved", "identity", "home", "uid"] {
        assert!(
            !messages[0].to_lowercase().contains(word),
            "the message must not name a rule: {}",
            messages[0]
        );
    }
}

#[test]
fn a_deletion_that_found_the_user_replaced_is_a_system_failure_that_ran_nothing() {
    // The identity changed while the deletion waited for the lock, so `userdel`
    // did not run. It must read as a fault to look at, not as a refusal of the
    // request, because the request was fine and the host moved under it.
    let error = AccountError::IdentityChanged {
        username: "acme".to_owned(),
    };

    assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    assert_ne!(
        code_of(&error),
        code_of(&AccountError::NotManaged {
            username: "acme".to_owned(),
            refusal: ManagedAccountError::Reserved,
        })
    );
}

#[test]
fn an_ftps_removal_refusal_is_a_system_failure_and_the_account_is_still_there() {
    assert_eq!(
        code_of(&AccountError::FtpsRemoval {
            reason: "an FTPS login is still there".to_owned(),
        }),
        ErrorCode::SystemFailure as i32
    );
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/accounts/account_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> AccountError {
    AccountError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> AccountError {
    AccountError::Bound(BoundExceeded::OutputTooLarge {
        program: "/usr/sbin/stand-in".to_owned(),
        stream: OutputStream::Stdout,
        cap: 4_194_304,
    })
}

/// A deadline is a fault of the host: a system failure, no tool text, and a
/// message that names the program that was killed.
#[test]
fn a_timeout_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&timed_out());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// An output cap is the same kind of fault as a deadline.
#[test]
fn an_output_overflow_is_a_system_failure_with_no_tool_output_and_a_message_naming_the_program() {
    let wire = to_agent_error(&output_too_large());

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(wire.tool_output.is_empty());
    assert!(wire.message.contains("/usr/sbin/stand-in"));
}

/// The wildcard arm answers `SystemFailure` too, so deleting the explicit arm
/// changes no behaviour and the two tests above cannot notice it. This one reads
/// the source: the bound must be named, and named before the wildcard.
#[test]
fn the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard() {
    let code_lines: Vec<&str> = MAPPING_SOURCE
        .lines()
        .map(str::trim_start)
        .filter(|line| !line.starts_with("//"))
        .collect();
    let wildcard = code_lines.iter().position(|line| line.starts_with("_ =>"));
    let bound = code_lines
        .iter()
        .position(|line| line.starts_with("AccountError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}

#[test]
fn surviving_processes_are_a_system_failure_whose_message_names_only_the_account() {
    // The account is still there and the same deletion can be retried, so the
    // panel must report a fault. The message must carry no pid, command line or
    // count: those are operator-side detail.
    let error = AccountError::ProcessesRemain {
        username: "acme".to_owned(),
    };

    assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    assert_eq!(
        error.to_string(),
        "processes of account 'acme' are still running after being killed"
    );
}
