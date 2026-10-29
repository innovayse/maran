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

use maran_ops::safe_write::SafeWriteError;
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
fn a_host_name_another_vhost_serves_is_already_exists_so_the_panel_reads_it_as_a_taken_domain() {
    // The panel answers this code on a create with "domain taken" and removes
    // nothing; VALIDATION_FAILED would read as a rendered configuration the
    // host refused, which no customer fixes by choosing another name.
    assert_eq!(
        code_of(&SitesOpError::ServerNameTaken {
            detail: "conflicting server name \"shop.example\"".to_owned(),
            stderr: "nginx: [warn] conflicting server name \"shop.example\"".to_owned(),
        }),
        ErrorCode::AlreadyExists as i32
    );
}

#[test]
fn a_name_the_request_repeats_is_invalid_input_and_not_a_claim_another_site_holds() {
    // The distinction the variant exists for: ALREADY_EXISTS would tell the
    // panel a name is taken on this host when the caller's own request is
    // wrong.
    assert_eq!(
        code_of(&SitesOpError::RepeatedHostName {
            name: "example.com".to_owned(),
        }),
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_refused_warning_reaches_the_panel_as_already_exists_with_nginxs_stderr() {
    let stderr = "nginx: [warn] conflicting server name \"shop.example\" on 0.0.0.0:80, ignored";
    let error = SitesOpError::from(SafeWriteError::RefusedWarning {
        warning: "conflicting server name \"shop.example\"".to_owned(),
        stderr: stderr.to_owned(),
    });

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::AlreadyExists as i32);
    assert_eq!(wire.tool_output, stderr);
    assert!(
        wire.message
            .contains("conflicting server name \"shop.example\""),
        "the operator's log line must name the host: {}",
        wire.message
    );
}

#[test]
fn a_target_that_already_existed_reaches_the_panel_as_already_exists_with_no_tool_output() {
    let error = SitesOpError::from(SafeWriteError::TargetExists {
        target: "/etc/maran/nginx/sites/shop.example.conf".to_owned(),
    });

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::AlreadyExists as i32);
    assert!(wire.tool_output.is_empty(), "no validator ran");
    assert!(
        wire.message
            .contains("/etc/maran/nginx/sites/shop.example.conf"),
        "{}",
        wire.message
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
fn only_the_failures_that_ran_a_tool_carry_that_tools_output() {
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
    // The third: a host name refused because nginx's own warning named it. The
    // validator ran and exited 0, and what it printed is the operator's
    // evidence.
    assert_eq!(
        to_agent_error(&SitesOpError::ServerNameTaken {
            detail: "conflicting server name \"shop.example\"".to_owned(),
            stderr: "nginx: [warn] conflicting server name \"shop.example\" on 0.0.0.0:80, ignored"
                .to_owned(),
        })
        .tool_output,
        "nginx: [warn] conflicting server name \"shop.example\" on 0.0.0.0:80, ignored"
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
        SitesOpError::ServerNameTaken {
            detail: "a vhost already exists at /etc/maran/nginx/sites/example.com.conf".to_owned(),
            stderr: String::new(),
        },
        SitesOpError::RepeatedHostName {
            name: "example.com".to_owned(),
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

/// A binary that could not be started reaches the panel saying so.
///
/// The end-to-end statement of the defect this variant was added for. The
/// operator's log line is built from `message`, so what matters is the sentence
/// the whole chain produces: `SafeWriteError::SpawnFailed` folds into
/// [`SitesOpError::ConfigWrite`] like every other protocol failure that is not a
/// refusal of the content, and its `Display` carries the program and the
/// operating system's reason through. It travels as a system failure, which is
/// what a missing binary is — a fault of the machine, not of the caller's input
/// — and it carries no `tool_output`, because no tool ran to produce any.
#[test]
fn a_program_that_could_not_be_started_reaches_the_panel_as_a_system_failure_naming_it() {
    let error = SitesOpError::from(SafeWriteError::SpawnFailed {
        program: "/usr/sbin/nginx".to_owned(),
        reason: "No such file or directory (os error 2)".to_owned(),
    });

    let wire = to_agent_error(&error);

    assert_eq!(wire.code, ErrorCode::SystemFailure as i32);
    assert!(
        wire.message.contains("could not run /usr/sbin/nginx"),
        "the operator's log line must name the program it could not run: {}",
        wire.message
    );
    assert!(
        !wire.message.contains("reload failed"),
        "a spawn failure must not read as a reload failure: {}",
        wire.message
    );
    assert!(
        wire.tool_output.is_empty(),
        "no tool ran, so none has output"
    );
}

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

/// The unit under test, read so that the arm that names the bound by itself can
/// be checked; see [`the_bound_variant_is_mapped_by_name_ahead_of_the_wildcard`].
const MAPPING_SOURCE: &str = include_str!("../../../services/sites/site_status.rs");

/// A deadline that passed, as the bounded spawn core reports it.
fn timed_out() -> SitesOpError {
    SitesOpError::Bound(BoundExceeded::TimedOut {
        program: "/usr/sbin/stand-in".to_owned(),
        deadline: Duration::from_secs(60),
    })
}

/// An output cap that was crossed, as the bounded spawn core reports it.
fn output_too_large() -> SitesOpError {
    SitesOpError::Bound(BoundExceeded::OutputTooLarge {
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
        .position(|line| line.starts_with("SitesOpError::Bound("));

    // Positive control: the scan does find the wildcard it is measured against.
    let wildcard = wildcard.expect("the mapping ends in a wildcard arm");
    assert!(bound.expect("the bound variant has its own arm") < wildcard);
}
