//! How a spawn failure and a config-write failure become errors of the mail area.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::spawn_error::SpawnError;

use crate::mail::mail_error::MailError;
use crate::mail::model::mail_daemon::MailDaemon;
use crate::safe_write::SafeWriteError;

fn a_bound() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: "/usr/bin/systemctl".to_owned(),
        deadline: Duration::from_secs(240),
    }
}

#[test]
fn a_bound_from_spawning_is_a_bound_and_not_a_program_that_never_started() {
    let mapped = MailError::from(SpawnError::Bound(a_bound()));

    assert_eq!(mapped, MailError::Bound(a_bound()));
    assert_ne!(mapped, MailError::program_unavailable());
}

#[test]
fn a_program_that_could_not_start_is_program_unavailable() {
    let mapped = MailError::from(SpawnError::Start {
        program: "/usr/sbin/postfix".to_owned(),
        reason: "No such file or directory".to_owned(),
    });

    assert_eq!(mapped, MailError::program_unavailable());
}

#[test]
fn a_refusing_check_names_the_daemon_and_carries_what_it_printed() {
    let mapped = MailError::from_config_write(
        SafeWriteError::ValidationFailed {
            stderr: "unknown parameter".to_owned(),
        },
        MailDaemon::Postfix,
        "postfix",
    );

    assert_eq!(
        mapped,
        MailError::ConfigRejected {
            daemon: MailDaemon::Postfix,
            output: "unknown parameter".to_owned(),
        }
    );
}

#[test]
fn a_refusing_reload_names_the_unit() {
    let mapped = MailError::from_config_write(
        SafeWriteError::ReloadFailed {
            stderr: String::new(),
        },
        MailDaemon::Dovecot,
        "dovecot",
    );

    assert_eq!(
        mapped,
        MailError::ServiceRefused {
            unit: "dovecot".to_owned()
        }
    );
}

#[test]
fn a_check_or_reload_that_could_not_start_is_program_unavailable() {
    let mapped = MailError::from_config_write(
        SafeWriteError::SpawnFailed {
            program: "/usr/bin/doveconf".to_owned(),
            reason: "not found".to_owned(),
        },
        MailDaemon::Dovecot,
        "dovecot",
    );

    assert_eq!(mapped, MailError::program_unavailable());
}

#[test]
fn a_config_write_that_times_out_is_a_bound_and_never_a_refusal() {
    let mapped = MailError::from_config_write(
        SafeWriteError::Bound(a_bound()),
        MailDaemon::Rspamd,
        "rspamd",
    );

    assert_eq!(mapped, MailError::Bound(a_bound()));
}

#[test]
fn a_mechanical_write_failure_carries_the_protocols_message() {
    let mapped =
        MailError::from_config_write(SafeWriteError::Rename, MailDaemon::Postfix, "postfix");

    assert!(matches!(mapped, MailError::ConfigWrite { .. }));
}

#[test]
fn every_remaining_variant_has_a_message_for_the_operator_log() {
    for error in [
        MailError::UnsupportedRelease,
        MailError::Render,
        MailError::ConfigUnreadable,
        MailError::SocketsUnreadable,
        MailError::HostnameNotUsable,
        MailError::NotListening { port: 25 },
        MailError::NotInstalled {
            daemon: MailDaemon::Rspamd,
        },
        MailError::CertificateMissing {
            domain: "mail.example.test".to_owned(),
            expected_path: "/p".to_owned(),
        },
    ] {
        assert!(!error.to_string().is_empty());
    }
}
