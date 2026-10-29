//! How the failures of other layers become errors of the certificate area.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::safe_write::SafeWriteError;
use crate::sites::SitesOpError;
use crate::ssl::ssl_op_error::SslOpError;

/// A bound as the spawn core reports one.
fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/bin/openssl".to_owned(),
        deadline: Duration::from_secs(60),
    }
}

/// Whether `error` is exactly the bound [`a_bound`] built.
fn is_the_bound(error: &SslOpError) -> bool {
    matches!(error, SslOpError::Bound(bound) if *bound == a_bound())
}

#[test]
fn a_bound_from_spawning_reaches_the_certificate_area_as_a_bound() {
    let mapped = SslOpError::from(SpawnError::Bound(a_bound()));

    assert!(is_the_bound(&mapped), "{mapped:?}");
}

#[test]
fn a_program_that_could_not_start_is_still_tool_unavailable() {
    // The inverse control: only the bound moves to its own variant.
    let mapped = SslOpError::from(SpawnError::Start {
        program: "/usr/bin/openssl".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert!(
        matches!(mapped, SslOpError::ToolUnavailable { .. }),
        "{mapped:?}"
    );
}

#[test]
fn an_input_that_could_not_be_delivered_names_the_program_and_nothing_of_the_input() {
    // The bytes being delivered are sometimes a private key.
    let mapped = SslOpError::from(SpawnError::Input {
        program: "/usr/bin/openssl".to_owned(),
    });

    let SslOpError::ToolUnavailable { reason } = mapped else {
        panic!("expected ToolUnavailable, got {mapped:?}");
    };
    assert!(reason.contains("/usr/bin/openssl"), "{reason}");
}

#[test]
fn a_bound_from_the_site_area_reaches_the_certificate_area_as_a_bound() {
    let mapped = SslOpError::from(SitesOpError::Bound(a_bound()));

    assert!(is_the_bound(&mapped), "{mapped:?}");
    assert!(
        !matches!(mapped, SslOpError::ConfigWrite { .. }),
        "a killed validator is not a write failure"
    );
}

#[test]
fn a_bound_from_the_config_protocol_reaches_the_certificate_area_as_a_bound() {
    let mapped = SslOpError::from(SafeWriteError::Bound(a_bound()));

    assert!(is_the_bound(&mapped), "{mapped:?}");
    assert!(
        !matches!(mapped, SslOpError::MaterialWrite { .. }),
        "a killed validator is not a write failure"
    );
}

#[test]
fn a_validator_that_ran_and_refused_is_still_an_nginx_validation_failure() {
    // The inverse control for the bound above.
    let mapped = SslOpError::from(SafeWriteError::ValidationFailed {
        stderr: "bad".to_owned(),
    });

    assert!(
        matches!(&mapped, SslOpError::NginxValidation { stderr } if stderr == "bad"),
        "{mapped:?}"
    );
}

#[test]
fn a_tool_run_that_timed_out_is_a_bound_and_never_tool_unavailable() {
    let mapped = SslOpError::from_tool_run(SafeWriteError::Bound(a_bound()));

    assert!(is_the_bound(&mapped), "{mapped:?}");
}

#[test]
fn a_tool_run_that_could_not_start_is_tool_unavailable_and_never_a_material_write() {
    let mapped = SslOpError::from_tool_run(SafeWriteError::SpawnFailed {
        program: "/usr/bin/openssl".to_owned(),
        reason: "gone".to_owned(),
    });

    assert!(
        matches!(mapped, SslOpError::ToolUnavailable { .. }),
        "{mapped:?}"
    );
}

#[test]
fn the_text_of_a_bound_names_the_program_and_never_claims_a_rollback() {
    let text = SslOpError::Bound(a_bound()).to_string();

    assert!(text.contains("/usr/bin/openssl"), "{text}");
    assert!(!text.to_lowercase().contains("rolled back"), "{text}");
}
