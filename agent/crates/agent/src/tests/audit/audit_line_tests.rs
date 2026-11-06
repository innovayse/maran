//! Tests for `audit_line`: the pinned text of the line and its emission.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use tonic::Code;

use super::AuditLine;
use crate::audit::audit_outcome::AuditOutcome;
use crate::audit::line_capture::LineCapture;
use crate::proto::ErrorCode;

/// A method entry as the mutating list spells one.
const ENTRY: &str = "/maran.agent.v1.SitesService/CreateSite";

#[test]
fn a_full_line_renders_every_field_in_order() {
    let line = AuditLine::new(
        ENTRY,
        AuditOutcome::Refused(ErrorCode::AlreadyExists),
        42,
        Some(AccountName::parse("alice_01").unwrap()),
        Some(Domain::parse("example.com").unwrap()),
    );

    assert_eq!(
        line.render(),
        "event=rpc method=maran.agent.v1.SitesService/CreateSite \
         outcome=ERROR_CODE_ALREADY_EXISTS duration_ms=42 account=alice_01 site=example.com"
    );
}

#[test]
fn an_absent_account_and_site_render_as_dashes() {
    let line = AuditLine::new(ENTRY, AuditOutcome::Unset, 0, None, None);

    assert_eq!(
        line.render(),
        "event=rpc method=maran.agent.v1.SitesService/CreateSite outcome=unset \
         duration_ms=0 account=- site=-"
    );
}

#[test]
fn a_transport_outcome_renders_with_its_prefix() {
    let line = AuditLine::new(
        ENTRY,
        AuditOutcome::Transport(Code::PermissionDenied),
        7,
        None,
        None,
    );

    assert!(
        line.render()
            .contains("outcome=transport:PermissionDenied ")
    );
}

#[test]
fn every_combination_of_field_states_renders_one_line_of_six_key_value_tokens() {
    let outcomes = [
        AuditOutcome::Ok,
        AuditOutcome::Refused(ErrorCode::SystemFailure),
        AuditOutcome::Unset,
        AuditOutcome::Transport(Code::Internal),
    ];
    let keys = [
        "event",
        "method",
        "outcome",
        "duration_ms",
        "account",
        "site",
    ];
    let mut rendered = 0;
    for outcome in outcomes {
        for account in [None, Some(AccountName::parse("alice_01").unwrap())] {
            for site in [None, Some(Domain::parse("example.com").unwrap())] {
                let text = AuditLine::new(ENTRY, outcome, 123, account.clone(), site).render();
                assert!(!text.contains(['\n', '\r', '\t']), "{text}");
                let tokens: Vec<&str> = text.split(' ').collect();
                assert_eq!(tokens.len(), 6, "{text}");
                for (token, key) in tokens.iter().zip(keys) {
                    let (name, value) = token.split_once('=').unwrap();
                    assert_eq!(name, key, "{text}");
                    assert!(!value.is_empty() && !value.contains('='), "{text}");
                }
                rendered += 1;
            }
        }
    }
    assert_eq!(
        rendered, 16,
        "positive control: every combination was rendered"
    );
}

#[test]
fn emitting_writes_one_info_event_on_the_audit_target_carrying_the_rendered_line() {
    let capture = LineCapture::start();
    let line = AuditLine::new(ENTRY, AuditOutcome::Ok, 5, None, None);

    line.emit();

    assert_eq!(capture.audit_lines(), vec![line.render()]);
    let raw = capture.raw_lines();
    assert_eq!(raw.len(), 1);
    assert!(raw[0].contains("INFO"), "{}", raw[0]);
}
