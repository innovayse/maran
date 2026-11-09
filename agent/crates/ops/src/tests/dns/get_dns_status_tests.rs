//! What the host is actually doing about DNS.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::dns::fake_dns_host::{FakeDnsHost, distro};
use crate::dns::get_dns_status::get_dns_status;
use crate::dns::model::dns_policy_observation::DnsPolicyObservation;

/// The two policy statements the panel promises, as the agent renders them.
const COMPLIANT: &str =
    "options {\n    recursion no;\n    rate-limit {\n        window 5;\n    };\n};\n";

#[test]
fn a_running_daemon_with_the_rendered_policy_is_reported_complete() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config(COMPLIANT);
    let state = get_dns_status(&host, distro()).unwrap();

    assert!(state.running);
    assert!(state.control_channel_answered);
    assert!(state.port_53_answering);
    assert!(state.enabled_at_boot);
    assert_eq!(state.recursion_refused, DnsPolicyObservation::InForce);
    assert_eq!(state.response_rate_limiting, DnsPolicyObservation::InForce);
}

#[test]
fn a_stopped_unit_is_not_asked_over_the_control_channel() {
    let host = FakeDnsHost::new().with_live_config(COMPLIANT);
    let state = get_dns_status(&host, distro()).unwrap();

    assert!(!state.running);
    assert!(!state.control_channel_answered);
    assert!(
        host.spawns()
            .iter()
            .all(|spawn| spawn.get(1).is_none_or(|first| first != "-k")),
        "rndc was run against a unit that is not active"
    );
}

#[test]
fn an_active_unit_over_a_daemon_that_does_not_answer_is_not_reported_as_answering() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_silent_daemon()
        .with_live_config(COMPLIANT);
    let state = get_dns_status(&host, distro()).unwrap();

    assert!(state.running);
    assert!(!state.control_channel_answered);
}

#[test]
fn a_configuration_that_allows_recursion_is_a_finding_and_not_unobserved() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n    recursion yes;\n};\n");
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::NotInForce);
}

#[test]
fn the_last_recursion_statement_wins_when_one_is_appended_after_the_rendered_one() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n    recursion no;\n    recursion yes;\n};\n");
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::NotInForce);
}

#[test]
fn the_last_recursion_statement_wins_in_the_other_direction_too() {
    // The inverse control: a refusal that comes last is the one in force.
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n    recursion yes;\n    recursion no;\n};\n");
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::InForce);
}

#[test]
fn a_commented_out_refusal_does_not_count() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n    // recursion no;\n};\n");
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::NotInForce);
}

#[test]
fn a_configuration_without_a_rate_limit_block_is_not_rate_limiting() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n    recursion no;\n};\n");
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(
        state.response_rate_limiting,
        DnsPolicyObservation::NotInForce
    );
}

#[test]
fn a_missing_configuration_certifies_nothing_either_way() {
    let host = FakeDnsHost::new();
    let state = get_dns_status(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::NotObserved);
    assert_eq!(
        state.response_rate_limiting,
        DnsPolicyObservation::NotObserved
    );
}

#[test]
fn a_unit_that_is_not_enabled_for_boot_is_reported_so() {
    let host = FakeDnsHost::new().with_live_config(COMPLIANT);
    let state = get_dns_status(&host, distro()).unwrap();

    assert!(!state.enabled_at_boot);
}
