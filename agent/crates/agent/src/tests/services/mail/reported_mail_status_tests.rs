//! An observed mail stack onto its wire status.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme as HostScheme;
use maran_ops::mail::{
    DaemonState, MailDaemon as HostDaemon, MailDetails, MailPorts, MailState, ObservedMail,
    PortBinding, PortState, ToolVersion,
};

use crate::proto::{MailDaemon, MailObservation, MailPasswordScheme};
use crate::services::mail::reported_mail_status::reported_mail_status;

/// A stack with every daemon installed and every port in `binding`, active or not.
fn observed(active: bool, binding: PortBinding) -> ObservedMail {
    ObservedMail {
        state: MailState {
            daemons: HostDaemon::START_ORDER
                .iter()
                .map(|&daemon| DaemonState {
                    daemon,
                    installed: true,
                    active,
                })
                .collect(),
            ports: MailPorts::ALL
                .iter()
                .map(|&port| PortState { port, binding })
                .collect(),
            effective_hostname: Some("mail.example.test".to_owned()),
            tls_configured: Some(true),
        },
        details: MailDetails {
            postfix_version: ToolVersion::parse_output("3.8.6"),
            dovecot_version: ToolVersion::parse_output("2.3.19.1 (9b53102964)"),
            rspamd_version: ToolVersion::parse_output("Rspamadm 3.8.4"),
            password_scheme_listed: Some(true),
            queue_messages: Some(7),
            sieve_available: Some(true),
        },
    }
}

fn daemon_of(
    status: &crate::proto::MailStatus,
    daemon: MailDaemon,
) -> &crate::proto::MailDaemonStatus {
    status
        .daemons
        .iter()
        .find(|each| each.daemon == daemon as i32)
        .unwrap()
}

#[test]
fn the_daemons_are_reported_in_the_order_of_the_wire_enum() {
    let status = reported_mail_status(&observed(true, PortBinding::Public), HostScheme::Argon2id);

    let order: Vec<i32> = status.daemons.iter().map(|each| each.daemon).collect();
    assert_eq!(
        order,
        [
            MailDaemon::Mta as i32,
            MailDaemon::ImapServer as i32,
            MailDaemon::SpamFilter as i32,
            MailDaemon::RateLimitStore as i32,
        ]
    );
}

#[test]
fn the_versions_reach_their_daemons_and_the_dovecot_generation_its_two_fields() {
    let status = reported_mail_status(&observed(true, PortBinding::Public), HostScheme::Argon2id);

    assert_eq!(daemon_of(&status, MailDaemon::Mta).version, "3.8.6");
    assert_eq!(
        daemon_of(&status, MailDaemon::ImapServer).version,
        "2.3.19.1"
    );
    assert_eq!(daemon_of(&status, MailDaemon::SpamFilter).version, "3.8.4");
    assert_eq!(daemon_of(&status, MailDaemon::RateLimitStore).version, "");
    assert_eq!((status.dovecot_major, status.dovecot_minor), (2, 3));
}

#[test]
fn listeners_follow_the_port_set_of_each_daemon_in_port_order() {
    let status = reported_mail_status(&observed(true, PortBinding::Public), HostScheme::Argon2id);

    let ports_of = |daemon| -> Vec<u32> {
        daemon_of(&status, daemon)
            .listeners
            .iter()
            .map(|listener| listener.port)
            .collect()
    };
    assert_eq!(ports_of(MailDaemon::Mta), [25, 465, 587]);
    assert_eq!(ports_of(MailDaemon::ImapServer), [143, 993, 110, 995]);
    assert!(ports_of(MailDaemon::SpamFilter).is_empty());
    assert!(ports_of(MailDaemon::RateLimitStore).is_empty());
    assert!(
        daemon_of(&status, MailDaemon::Mta)
            .listeners
            .iter()
            .all(|listener| listener.address == "*")
    );
}

#[test]
fn a_loopback_only_port_is_reported_with_the_loopback_address_and_an_unbound_one_not_at_all() {
    let mut stack = observed(true, PortBinding::Public);
    stack.state.ports = vec![
        PortState {
            port: 25,
            binding: PortBinding::LoopbackOnly,
        },
        PortState {
            port: 465,
            binding: PortBinding::NotBound,
        },
        PortState {
            port: 587,
            binding: PortBinding::Public,
        },
    ];

    let status = reported_mail_status(&stack, HostScheme::Argon2id);

    let mta = daemon_of(&status, MailDaemon::Mta);
    let seen: Vec<(String, u32)> = mta
        .listeners
        .iter()
        .map(|listener| (listener.address.clone(), listener.port))
        .collect();
    assert_eq!(seen, [("127.0.0.1".to_owned(), 25), ("*".to_owned(), 587)]);
}

#[test]
fn only_the_daemons_whose_ports_were_read_claim_to_have_observed_their_listeners() {
    let status = reported_mail_status(&observed(true, PortBinding::Public), HostScheme::Argon2id);

    assert_eq!(
        daemon_of(&status, MailDaemon::Mta).listeners_observed,
        MailObservation::Yes as i32
    );
    assert_eq!(
        daemon_of(&status, MailDaemon::ImapServer).listeners_observed,
        MailObservation::Yes as i32
    );
    assert_eq!(
        daemon_of(&status, MailDaemon::SpamFilter).listeners_observed,
        MailObservation::NotObserved as i32
    );
    assert_eq!(
        daemon_of(&status, MailDaemon::RateLimitStore).listeners_observed,
        MailObservation::NotObserved as i32
    );
}

#[test]
fn a_stack_is_enabled_only_when_every_daemon_is_active() {
    let up = reported_mail_status(&observed(true, PortBinding::Public), HostScheme::Argon2id);
    let down = reported_mail_status(
        &observed(false, PortBinding::NotBound),
        HostScheme::Argon2id,
    );
    let mut partial = observed(true, PortBinding::Public);
    partial.state.daemons[2].active = false;
    let partial = reported_mail_status(&partial, HostScheme::Argon2id);

    assert_eq!(up.enabled, MailObservation::Yes as i32);
    assert_eq!(down.enabled, MailObservation::No as i32);
    assert_eq!(partial.enabled, MailObservation::No as i32);
}

#[test]
fn the_hostname_and_the_tls_reading_are_the_hosts_and_an_unread_one_is_not_observed() {
    let mut stack = observed(true, PortBinding::Public);
    let read = reported_mail_status(&stack, HostScheme::Argon2id);
    stack.state.effective_hostname = None;
    stack.state.tls_configured = None;
    let unread = reported_mail_status(&stack, HostScheme::Argon2id);

    assert_eq!(read.effective_hostname, "mail.example.test");
    assert_eq!(read.tls_in_use, MailObservation::Yes as i32);
    assert_eq!(unread.effective_hostname, "");
    assert_eq!(unread.tls_in_use, MailObservation::NotObserved as i32);
}

#[test]
fn the_password_scheme_is_the_hosts_and_whether_the_daemon_agrees_is_its_own_field() {
    let mut stack = observed(true, PortBinding::Public);
    stack.details.password_scheme_listed = Some(false);

    let argon = reported_mail_status(&stack, HostScheme::Argon2id);
    let blowfish = reported_mail_status(&stack, HostScheme::BlowfishCrypt);

    assert_eq!(argon.password_scheme, MailPasswordScheme::Argon2id as i32);
    assert_eq!(
        blowfish.password_scheme,
        MailPasswordScheme::BlfCrypt as i32
    );
    assert_eq!(argon.password_scheme_verified, MailObservation::No as i32);
}

#[test]
fn without_an_imap_server_no_scheme_is_named() {
    let mut stack = observed(false, PortBinding::NotBound);
    stack.state.daemons[2].installed = false;
    stack.details.dovecot_version = None;
    stack.details.password_scheme_listed = None;

    let status = reported_mail_status(&stack, HostScheme::Argon2id);

    assert_eq!(
        status.password_scheme,
        MailPasswordScheme::Unspecified as i32
    );
    assert_eq!(
        status.password_scheme_verified,
        MailObservation::NotObserved as i32
    );
    assert_eq!((status.dovecot_major, status.dovecot_minor), (0, 0));
    assert_eq!(
        daemon_of(&status, MailDaemon::ImapServer).installed,
        MailObservation::No as i32
    );
}

#[test]
fn an_unread_queue_is_absent_and_an_empty_one_is_zero() {
    let mut stack = observed(true, PortBinding::Public);
    stack.details.queue_messages = Some(0);
    let empty = reported_mail_status(&stack, HostScheme::Argon2id);
    stack.details.queue_messages = None;
    let unread = reported_mail_status(&stack, HostScheme::Argon2id);

    assert_eq!(empty.queue.unwrap().messages, 0);
    assert!(unread.queue.is_none());
}

#[test]
fn sieve_is_three_valued() {
    let mut stack = observed(true, PortBinding::Public);
    let mut seen = Vec::new();
    for reading in [Some(true), Some(false), None] {
        stack.details.sieve_available = reading;
        seen.push(reported_mail_status(&stack, HostScheme::Argon2id).sieve_available);
    }

    assert_eq!(
        seen,
        [
            MailObservation::Yes as i32,
            MailObservation::No as i32,
            MailObservation::NotObserved as i32
        ]
    );
}
