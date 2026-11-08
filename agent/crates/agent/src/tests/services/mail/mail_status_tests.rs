//! The mapping from mail failures onto the wire error.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_ops::mail::{MailDaemon, MailError};

use crate::proto::ErrorCode;
use crate::services::mail::mail_status::to_agent_error;

#[test]
fn a_certificate_the_operator_can_install_is_not_found() {
    let error = to_agent_error(&MailError::CertificateMissing {
        domain: "mail.example.test".to_owned(),
        expected_path: "/etc/maran/certs/mail.example.test/fullchain.pem".to_owned(),
    });

    assert_eq!(error.code, ErrorCode::NotFound as i32);
    assert!(error.message.contains("mail.example.test"));
    assert!(error.tool_output.is_empty());
}

#[test]
fn a_hostname_the_certificate_store_cannot_use_is_invalid_input() {
    assert_eq!(
        to_agent_error(&MailError::HostnameNotUsable).code,
        ErrorCode::InvalidInput as i32
    );
}

#[test]
fn a_configuration_a_daemon_refused_is_a_validation_failure() {
    let error = to_agent_error(&MailError::ConfigRejected {
        daemon: MailDaemon::Postfix,
        output: "main.cf: unknown parameter".to_owned(),
    });

    assert_eq!(error.code, ErrorCode::ValidationFailed as i32);
    assert!(error.message.contains("unknown parameter"));
    assert!(error.tool_output.is_empty());
}

#[test]
fn a_release_the_stack_cannot_be_configured_for_is_not_implemented() {
    assert_eq!(
        to_agent_error(&MailError::UnsupportedRelease).code,
        ErrorCode::NotImplemented as i32
    );
}

#[test]
fn every_fault_of_the_machine_is_a_system_failure() {
    let faults = [
        MailError::NotInstalled {
            daemon: MailDaemon::Rspamd,
        },
        MailError::ServiceRefused {
            unit: "postfix".to_owned(),
        },
        MailError::NotListening { port: 25 },
        MailError::SpawnFailed { code: -1 },
        MailError::Bound(BoundExceeded::TimedOut {
            program: "/usr/sbin/postfix".to_owned(),
            deadline: Duration::from_secs(60),
        }),
        MailError::Render,
        MailError::ConfigUnreadable,
        MailError::ConfigWrite {
            reason: "disk full".to_owned(),
        },
        MailError::SocketsUnreadable,
    ];

    for fault in faults {
        assert_eq!(
            to_agent_error(&fault).code,
            ErrorCode::SystemFailure as i32,
            "{fault:?}"
        );
    }
}
