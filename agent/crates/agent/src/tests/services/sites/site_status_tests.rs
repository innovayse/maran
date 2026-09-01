//! Tests for the wire code every site failure travels as.
//!
//! One assertion per variant of `SitesOpError`, because the codes are a
//! CONTRACT — `sites.proto` names them to callers — and because
//! `rules/testing.md` requires every typed error variant to appear in at least
//! one test. Before this file six of the eleven variants appeared nowhere in
//! the suite, so flipping the code a rolled-back `nginx -t` refusal travels as
//! survived the whole workspace.
//!
//! The second thing asserted here is `tool_output`. It carries a failing
//! program's stderr and is operator-facing by contract; a variant that started
//! carrying it where the panel does not expect it would put tool text on a
//! path the panel treats as customer-safe (rules/security.md item 8).

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::sites::SitesOpError;

use super::to_agent_error;
use crate::proto::ErrorCode;

/// The code `error` is reported as.
fn code_of(error: &SitesOpError) -> i32 {
    to_agent_error(error).code
}

#[test]
fn a_domain_already_configured_here_is_already_exists_so_a_retried_create_reads_as_done() {
    assert_eq!(
        code_of(&SitesOpError::AlreadyExists {
            domain: "example.com".to_owned(),
        }),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_domain_that_is_not_configured_here_is_not_found_so_a_retried_delete_reads_as_done() {
    assert_eq!(
        code_of(&SitesOpError::NotFound {
            domain: "example.com".to_owned(),
        }),
        ErrorCode::NotFound as i32
    );
}

#[test]
fn a_config_the_validator_refused_is_a_validation_failure_because_the_state_was_rolled_back() {
    assert_eq!(
        code_of(&SitesOpError::NginxValidation {
            stderr: "nginx: [emerg] unknown directive".to_owned(),
        }),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_php_version_that_is_not_installed_is_a_validation_failure_as_sites_proto_states() {
    assert_eq!(
        code_of(&SitesOpError::PhpVersionNotInstalled {
            version: "8.4".to_owned(),
        }),
        ErrorCode::ValidationFailed as i32
    );
}

#[test]
fn a_document_root_outside_the_home_is_invalid_input_because_the_caller_named_it() {
    // Not a system failure: the machine is fine and the request is not. An
    // operator sent looking at the host would find nothing wrong with it.
    assert_eq!(
        code_of(&SitesOpError::UnsafeDocumentRoot {
            reason: "resolved outside /home/alice".to_owned(),
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn every_fault_of_this_machine_is_a_system_failure_and_none_of_them_is_reported_as_not_found() {
    for error in [
        SitesOpError::ReloadFailed {
            stderr: "job for nginx.service failed".to_owned(),
        },
        SitesOpError::Render {
            reason: "template not found".to_owned(),
        },
        SitesOpError::ConfigWrite {
            reason: "no space left on device".to_owned(),
        },
        SitesOpError::DocumentRoot {
            reason: "the forked child exited 1".to_owned(),
        },
        SitesOpError::ConfigUnreadable {
            path: "/etc/nginx/maran.d".to_owned(),
        },
        SitesOpError::LogUnreadable {
            path: "/home/alice/logs/example.com.access.log".to_owned(),
        },
    ] {
        assert_eq!(code_of(&error), ErrorCode::SystemFailure as i32);
    }
}

#[test]
fn only_the_two_failures_that_ran_a_tool_carry_that_tools_output() {
    assert_eq!(
        to_agent_error(&SitesOpError::NginxValidation {
            stderr: "nginx: configuration file test failed".to_owned(),
        })
        .tool_output,
        "nginx: configuration file test failed"
    );
    assert_eq!(
        to_agent_error(&SitesOpError::ReloadFailed {
            stderr: "job for nginx.service failed".to_owned(),
        })
        .tool_output,
        "job for nginx.service failed"
    );
}

#[test]
fn a_failure_that_ran_no_tool_carries_no_tool_output_at_all() {
    for error in [
        SitesOpError::AlreadyExists {
            domain: "example.com".to_owned(),
        },
        SitesOpError::NotFound {
            domain: "example.com".to_owned(),
        },
        SitesOpError::PhpVersionNotInstalled {
            version: "8.4".to_owned(),
        },
        SitesOpError::UnsafeDocumentRoot {
            reason: "resolved outside /home/alice".to_owned(),
        },
        SitesOpError::Render {
            reason: "template not found".to_owned(),
        },
        SitesOpError::ConfigWrite {
            reason: "no space left on device".to_owned(),
        },
        SitesOpError::DocumentRoot {
            reason: "the forked child exited 1".to_owned(),
        },
        SitesOpError::ConfigUnreadable {
            path: "/etc/nginx/maran.d".to_owned(),
        },
        SitesOpError::LogUnreadable {
            path: "/home/alice/logs/example.com.access.log".to_owned(),
        },
    ] {
        assert!(to_agent_error(&error).tool_output.is_empty());
    }
}

#[test]
fn the_message_is_the_failures_own_display_and_never_a_sentence_invented_here() {
    let error = SitesOpError::NotFound {
        domain: "example.com".to_owned(),
    };

    assert_eq!(to_agent_error(&error).message, error.to_string());
}
