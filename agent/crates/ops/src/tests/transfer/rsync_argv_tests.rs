//! Tests for [`rsync_argv`]: the controls of the threat note are in every run,
//! the phases differ in exactly one option, and nothing comes from a caller as
//! text.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use maran_agent_core::validation::fs::remote_path::RemotePath;
use maran_agent_core::validation::system::bandwidth_limit::BandwidthLimit;
use maran_agent_core::validation::system::name::AccountName;

use super::rsync_argv;
use crate::transfer::model::pull_phase::PullPhase;
use crate::transfer::model::pull_request::PullRequest;
use crate::transfer::transfer_fixtures::{credential, endpoint_at};

/// A pull of alice's home.
fn request(phase: PullPhase, bandwidth: Option<u32>, home: &str) -> PullRequest {
    PullRequest {
        endpoint: endpoint_at("source.example.org"),
        credential: credential(),
        account: AccountName::parse("alice").unwrap(),
        source_home: RemotePath::parse(home).unwrap(),
        phase,
        bandwidth: bandwidth.map(|figure| BandwidthLimit::parse(figure).unwrap()),
        expected_bytes: 0,
    }
}

fn v4() -> IpAddr {
    "198.51.100.20".parse().unwrap()
}

#[test]
fn every_run_carries_the_controls_of_the_threat_note() {
    for phase in [PullPhase::Initial, PullPhase::Final] {
        let argv = rsync_argv(&request(phase, None, "/home/alice"), "ssh -x", v4());

        for control in [
            "--no-devices",
            "--no-specials",
            "--safe-links",
            "--chmod=a-s,a-t",
            "--partial",
            "--max-size=64G",
            "--timeout=600",
            "--rsh=ssh -x",
        ] {
            assert!(
                argv.contains(&control.to_owned()),
                "{phase:?}: {control} missing"
            );
        }
        // Hard links are not preserved, because the option that would is never given.
        assert!(
            !argv
                .iter()
                .any(|argument| argument == "--hard-links" || argument == "-H")
        );
        // No ownership is requested of an unprivileged receiver.
        assert!(
            !argv
                .iter()
                .any(|argument| argument == "--owner" || argument == "--group" || argument == "-a")
        );
    }
}

#[test]
fn only_the_final_phase_deletes() {
    let initial = rsync_argv(
        &request(PullPhase::Initial, None, "/home/alice"),
        "ssh",
        v4(),
    );
    let last = rsync_argv(&request(PullPhase::Final, None, "/home/alice"), "ssh", v4());

    assert!(!initial.contains(&"--delete".to_owned()));
    assert!(last.contains(&"--delete".to_owned()));
}

#[test]
fn a_limit_becomes_the_bandwidth_option_and_none_leaves_it_out() {
    let limited = rsync_argv(
        &request(PullPhase::Initial, Some(512), "/home/alice"),
        "ssh",
        v4(),
    );
    let free = rsync_argv(
        &request(PullPhase::Initial, None, "/home/alice"),
        "ssh",
        v4(),
    );

    assert!(limited.contains(&"--bwlimit=512".to_owned()));
    assert!(
        !free
            .iter()
            .any(|argument| argument.starts_with("--bwlimit"))
    );
}

#[test]
fn the_operands_follow_the_end_of_options_and_are_the_resolved_address_and_the_account_home() {
    let argv = rsync_argv(
        &request(PullPhase::Initial, None, "/home/alice"),
        "ssh",
        v4(),
    );

    let end = argv.iter().position(|argument| argument == "--").unwrap();
    assert_eq!(argv.len(), end + 3);
    assert_eq!(argv[end + 1], "root@198.51.100.20:/home/alice/");
    assert_eq!(argv[end + 2], "/home/alice/");
    // Nothing that is an option comes after the separator.
    assert!(
        argv[..end]
            .iter()
            .all(|argument| argument.starts_with("--"))
    );
}

#[test]
fn a_home_already_ending_in_a_slash_gets_no_second_one() {
    let argv = rsync_argv(
        &request(PullPhase::Initial, None, "/home/alice/"),
        "ssh",
        v4(),
    );

    assert_eq!(argv[argv.len() - 2], "root@198.51.100.20:/home/alice/");
}

#[test]
fn an_ipv6_source_is_bracketed() {
    let address: IpAddr = "2001:db8::10".parse().unwrap();

    let argv = rsync_argv(
        &request(PullPhase::Initial, None, "/home/alice"),
        "ssh",
        address,
    );

    assert_eq!(argv[argv.len() - 2], "root@[2001:db8::10]:/home/alice/");
}

#[test]
fn the_destination_is_built_from_the_account_name_and_the_home_root() {
    let mut other = request(PullPhase::Initial, None, "/home/alice");
    other.account = AccountName::parse("bob_site").unwrap();

    let argv = rsync_argv(&other, "ssh", v4());

    assert_eq!(argv[argv.len() - 1], "/home/bob_site/");
}
