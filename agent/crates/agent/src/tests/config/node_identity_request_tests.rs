//! Tests for the `node_identity_request` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::Path;

use maran_agent_core::validation::fleet::node_address_error::NodeAddressError;

use super::{NodeIdentityRequest, OptionsError};

/// Builds an argument vector from string literals.
fn arguments(values: &[&str]) -> Vec<String> {
    values.iter().map(|value| (*value).to_owned()).collect()
}

#[test]
fn a_directory_and_an_address_are_enough_and_replacing_is_off_by_default() {
    let request = NodeIdentityRequest::parse(&arguments(&[
        "/etc/maran/fleet",
        "--node-address",
        "node1.example.com",
    ]))
    .unwrap();

    assert_eq!(request.directory, Path::new("/etc/maran/fleet"));
    assert!(!request.replace);
}

#[test]
fn replace_is_on_only_when_asked_for_and_the_flags_may_come_in_either_order() {
    let request = NodeIdentityRequest::parse(&arguments(&[
        "/etc/maran/fleet",
        "--replace",
        "--node-address",
        "203.0.113.7",
    ]))
    .unwrap();

    assert!(request.replace);
}

#[test]
fn a_missing_directory_is_refused() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&[])),
        Err(OptionsError::MissingValue {
            flag: "--generate-node-identity"
        })
    );
}

#[test]
fn a_relative_directory_is_refused() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&["fleet", "--node-address", "203.0.113.7"])),
        Err(OptionsError::RelativePath {
            flag: "--generate-node-identity",
            value: "fleet".to_owned()
        })
    );
}

#[test]
fn a_run_without_an_address_is_refused_rather_than_defaulted() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&["/etc/maran/fleet"])),
        Err(OptionsError::MissingValue {
            flag: "--node-address"
        })
    );
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&["/etc/maran/fleet", "--node-address"])),
        Err(OptionsError::MissingValue {
            flag: "--node-address"
        })
    );
}

#[test]
fn an_address_that_cannot_be_dialled_is_refused_with_the_reason() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&[
            "/etc/maran/fleet",
            "--node-address",
            "0.0.0.0"
        ])),
        Err(OptionsError::InvalidNodeAddress {
            reason: NodeAddressError::NotDialable {
                value: "0.0.0.0".to_owned()
            }
        })
    );
}

#[test]
fn a_repeated_address_or_replace_is_refused_rather_than_last_wins() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&[
            "/d",
            "--node-address",
            "203.0.113.7",
            "--node-address",
            "203.0.113.8"
        ])),
        Err(OptionsError::RepeatedFlag {
            flag: "--node-address"
        })
    );
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&[
            "/d",
            "--node-address",
            "203.0.113.7",
            "--replace",
            "--replace"
        ])),
        Err(OptionsError::RepeatedFlag { flag: "--replace" })
    );
}

#[test]
fn a_daemon_flag_after_the_directory_is_an_unknown_flag() {
    assert_eq!(
        NodeIdentityRequest::parse(&arguments(&[
            "/d",
            "--node-address",
            "203.0.113.7",
            "--socket",
            "/run/x.sock"
        ])),
        Err(OptionsError::UnknownFlag {
            flag: "--socket".to_owned()
        })
    );
}
