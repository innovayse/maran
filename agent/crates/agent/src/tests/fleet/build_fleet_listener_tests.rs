//! Tests for turning the fleet flags into a listener.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::fleet::listen_address::ListenAddress;

use super::build_fleet_listener;
use crate::config::fleet_options::FleetOptions;
use crate::fleet::fixture_identity::{generate_into, own_uid, private_directory};
use crate::fleet::fleet_identity_error::FleetIdentityError;
use crate::fleet::fleet_listener_error::FleetListenerError;

/// The options for a node whose identity lives in `directory`.
fn options(directory: &std::path::Path) -> FleetOptions {
    FleetOptions {
        listen: ListenAddress::parse("127.0.0.1:18444").unwrap(),
        directory: directory.to_path_buf(),
    }
}

#[test]
fn a_directory_holding_a_generated_identity_makes_a_listener() {
    let directory = private_directory();
    generate_into(directory.path());

    let listener = build_fleet_listener(&options(directory.path()), own_uid());

    assert!(listener.is_ok());
}

#[test]
fn a_directory_that_does_not_exist_stops_the_listener_from_being_built() {
    let directory = private_directory();
    let missing = directory.path().join("not-there");

    let outcome = build_fleet_listener(&options(&missing), own_uid());

    assert!(matches!(
        outcome,
        Err(FleetListenerError::IdentityFiles(
            FleetIdentityError::DirectoryMissing { .. }
        ))
    ));
}

#[test]
fn a_directory_with_no_identity_in_it_stops_the_listener_from_being_built() {
    let directory = private_directory();

    let outcome = build_fleet_listener(&options(directory.path()), own_uid());

    assert!(matches!(
        outcome,
        Err(FleetListenerError::IdentityFiles(
            FleetIdentityError::FileMissing { .. }
        ))
    ));
}
