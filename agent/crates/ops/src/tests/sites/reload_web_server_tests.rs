//! Tests for [`reload_web_server`].
//!
//! What is pinned is what the operation DECIDES: the validator and the reload it
//! hands the protocol, with the class each runs under and the end-of-options guard
//! before the unit name, and that a program killed at its deadline surfaces as a
//! bound. The protocol itself is driven by `safe_write`'s tests.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;

use super::reload_web_server;
use crate::sites::SitesOpError;
use crate::sites::fake_site_host::{FakeSiteHost, ProtocolCall, distro};

/// What the operation must hand the protocol, spelled out and not built by the code
/// under test.
fn expected_call() -> ProtocolCall {
    ProtocolCall {
        validator: (distro().nginx_binary().to_string(), vec!["-t".to_owned()]),
        validator_class: CallClass::Validate,
        reload: (
            distro().service_manager().to_string(),
            vec![
                "reload".to_owned(),
                "--".to_owned(),
                distro().nginx_service().to_owned(),
            ],
        ),
    }
}

#[test]
fn reloading_the_web_server_hands_the_protocol_a_short_class_validator_and_a_reload_ended_with_end_of_options()
 {
    let host = FakeSiteHost::passing();

    reload_web_server(&host, distro()).unwrap();

    assert_eq!(host.protocol(), vec![expected_call()]);
}

#[test]
fn reloading_the_web_server_whose_validator_times_out_reports_the_bound_and_never_a_refusal() {
    // `NginxValidation` says nginx read the tree and rejected it; a validator killed at
    // its deadline said nothing about the tree.
    let host = FakeSiteHost::passing();
    host.time_out_validation();

    let refusal = reload_web_server(&host, distro());

    assert!(
        matches!(refusal, Err(SitesOpError::Bound(_))),
        "expected Bound, got {refusal:?}"
    );
}

#[test]
fn reloading_the_web_server_with_a_validator_that_answers_is_not_a_bound() {
    // The inverse control: the same call against a validator that refuses is a
    // refusal, so the bound above is the timeout and not a blanket failure.
    let host = FakeSiteHost::passing();
    host.reject_validation("unknown directive");

    let refusal = reload_web_server(&host, distro());

    assert!(
        matches!(refusal, Err(SitesOpError::NginxValidation { .. })),
        "expected NginxValidation, got {refusal:?}"
    );
}
