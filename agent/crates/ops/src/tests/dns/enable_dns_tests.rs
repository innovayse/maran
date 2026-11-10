//! Turning DNS on: render, swap, check, restart, observe — and put the previous
//! configuration back if the daemon does not answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::agent_paths::AgentPaths;

use crate::dns::dns_error::DnsError;
use crate::dns::enable_dns::enable_dns;
use crate::dns::fake_dns_host::{FakeDnsHost, distro};
use crate::dns::model::dns_policy_observation::DnsPolicyObservation;

/// The configuration an enable on a fresh host renders, found by running one.
fn rendered() -> String {
    let host = FakeDnsHost::new();
    enable_dns(&host, distro()).unwrap();
    host.live_config().unwrap()
}

/// The position of the first spawn whose argv contains `word`.
fn position_of(host: &FakeDnsHost, word: &str) -> usize {
    host.spawns()
        .iter()
        .position(|spawn| spawn.iter().any(|part| part == word))
        .unwrap_or_else(|| panic!("nothing was run with {word}"))
}

#[test]
fn enabling_on_a_fresh_host_writes_the_configuration_starts_the_daemon_and_boots_it() {
    let host = FakeDnsHost::new();
    let state = enable_dns(&host, distro()).unwrap();

    assert_eq!(host.written_configs().len(), 1);
    assert!(state.running);
    assert!(state.control_channel_answered);
    assert!(state.port_53_answering);
    assert!(state.enabled_at_boot);
    assert!(host.is_enabled());
}

#[test]
fn the_daemons_own_checker_reads_the_file_at_its_real_path_before_the_restart() {
    let host = FakeDnsHost::new();
    enable_dns(&host, distro()).unwrap();

    let checked = position_of(&host, AgentPaths::DNS_CONFIG_PATH);
    let restarted = position_of(&host, "restart");
    assert!(checked < restarted, "the restart ran before the checker");
    assert_eq!(host.spawns()[checked][0], "/usr/bin/named-checkconf");
}

#[test]
fn the_rendered_configuration_refuses_recursion_and_rate_limits() {
    let host = FakeDnsHost::new();
    let state = enable_dns(&host, distro()).unwrap();

    assert_eq!(state.recursion_refused, DnsPolicyObservation::InForce);
    assert_eq!(state.response_rate_limiting, DnsPolicyObservation::InForce);
}

#[test]
fn enabling_again_on_a_converged_daemon_writes_nothing_and_restarts_nothing() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config(&rendered());
    let state = enable_dns(&host, distro()).unwrap();

    assert!(host.written_configs().is_empty());
    assert_eq!(host.restart_count(), 0);
    assert!(state.running);
}

#[test]
fn the_same_bytes_on_disk_with_the_daemon_stopped_are_not_converged() {
    let host = FakeDnsHost::new().with_live_config(&rendered());
    let state = enable_dns(&host, distro()).unwrap();

    assert_eq!(host.restart_count(), 1);
    assert!(state.running);
}

#[test]
fn a_healthy_daemon_missing_only_from_the_boot_sequence_is_enabled_without_a_restart() {
    let host = FakeDnsHost::new()
        .with_running_unit_not_enabled()
        .with_live_config(&rendered());
    let state = enable_dns(&host, distro()).unwrap();

    assert!(host.is_enabled());
    assert!(state.enabled_at_boot);
    assert_eq!(host.restart_count(), 0);
    assert!(host.written_configs().is_empty());
}

#[test]
fn a_configuration_the_checker_refuses_is_never_restarted_onto_and_is_taken_back() {
    let host = FakeDnsHost::new().with_checker_refusing("named.conf:1: syntax error");
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(
        error,
        DnsError::ConfigRejected {
            output: "named.conf:1: syntax error".to_owned()
        }
    );
    assert_eq!(host.restart_count(), 0);
    assert_eq!(host.live_config(), None);
    assert!(!host.is_enabled());
}

#[test]
fn a_refused_configuration_over_a_previous_one_leaves_the_previous_one() {
    let host = FakeDnsHost::new()
        .with_running_daemon()
        .with_live_config("options {\n};\n")
        .with_checker_refusing("refused");
    enable_dns(&host, distro()).unwrap_err();

    assert_eq!(host.live_config().as_deref(), Some("options {\n};\n"));
}

#[test]
fn a_unit_that_will_not_start_over_a_previous_configuration_is_restarted_onto_it() {
    let host = FakeDnsHost::new()
        .with_live_config("options {\n};\n")
        .with_failing_restart();
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(
        error,
        DnsError::ServiceRefused {
            unit: "maran-dns.service".to_owned()
        }
    );
    assert_eq!(host.live_config().as_deref(), Some("options {\n};\n"));
    assert_eq!(host.restart_count(), 2, "the daemon was not put back");
}

#[test]
fn a_unit_that_will_not_start_on_a_fresh_host_leaves_no_file_and_is_not_restarted_again() {
    let host = FakeDnsHost::new().with_failing_restart();
    enable_dns(&host, distro()).unwrap_err();

    assert_eq!(host.live_config(), None);
    assert_eq!(host.restart_count(), 1);
}

#[test]
fn a_daemon_that_never_answers_is_reported_and_a_fresh_host_is_put_back() {
    let host = FakeDnsHost::new().with_silent_daemon();
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(error, DnsError::NotListening);
    assert_eq!(host.live_config(), None);
    assert!(!host.is_running());
    let waited: Duration = host.pauses().iter().sum();
    assert!(waited >= Duration::from_secs(10));
}

#[test]
fn a_daemon_that_never_answers_over_a_previous_configuration_gets_the_previous_one_back() {
    let host = FakeDnsHost::new()
        .with_live_config("options {\n};\n")
        .with_silent_daemon();
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(error, DnsError::NotListening);
    assert_eq!(host.live_config().as_deref(), Some("options {\n};\n"));
}

#[test]
fn a_rollback_never_reverts_a_newer_write_made_by_another_operation() {
    let host = FakeDnsHost::new()
        .with_silent_daemon()
        .with_neighbour_write_after_the_swap("options {\n    // theirs\n};\n");
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(error, DnsError::NotListening);
    assert_eq!(
        host.live_config().as_deref(),
        Some("options {\n    // theirs\n};\n")
    );
}

#[test]
fn a_unit_that_cannot_be_enabled_for_boot_is_an_error() {
    let host = FakeDnsHost::new().with_failing_enable();
    let error = enable_dns(&host, distro()).unwrap_err();

    assert_eq!(
        error,
        DnsError::ServiceRefused {
            unit: "maran-dns.service".to_owned()
        }
    );
}

#[test]
fn enabling_never_runs_a_firewall_program() {
    let host = FakeDnsHost::new();
    enable_dns(&host, distro()).unwrap();

    assert!(
        host.spawns()
            .iter()
            .all(|spawn| !spawn[0].ends_with("/nft")),
        "enable touched the firewall: {:?}",
        host.spawns()
    );
}

#[test]
fn a_daemon_that_accepts_a_policy_is_rendered_with_the_policy_and_reported_able_to_sign() {
    let host = FakeDnsHost::new();
    let state = enable_dns(&host, distro()).unwrap();

    assert!(state.dnssec_supported);
    assert!(
        host.live_config()
            .unwrap()
            .lines()
            .any(|line| line.starts_with("dnssec-policy \"maran-csk\"")),
        "the supported daemon's configuration states no policy"
    );
}

#[test]
fn a_daemon_that_refuses_a_policy_is_rendered_without_one_and_reported_unable_to_sign() {
    // The planted failure: a build without the statement. Rendering the policy
    // unconditionally would make the daemon's checker refuse the whole file.
    let host = FakeDnsHost::new().with_dnssec_unsupported();
    let state = enable_dns(&host, distro()).unwrap();

    assert!(!state.dnssec_supported);
    assert!(
        !host.live_config().unwrap().contains("dnssec-policy"),
        "an unsupported daemon's configuration still states a policy"
    );
}

#[test]
fn the_support_is_probed_before_the_configuration_is_written() {
    let host = FakeDnsHost::new();
    enable_dns(&host, distro()).unwrap();

    let probed = position_of(&host, "/etc/maran/bind/dnssec-probe.conf");
    let written = position_of(&host, AgentPaths::DNS_CONFIG_PATH);
    assert!(probed < written, "the probe ran after the real file");
    assert!(
        host.probes()[0].contains("dnssec-policy \"maran-csk\""),
        "the probe does not state the policy"
    );
}
