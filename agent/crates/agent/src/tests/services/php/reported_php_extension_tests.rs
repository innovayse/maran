//! Tests for one extension reading onto its wire payload.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_ops::php::{ExtensionAvailability, PhpExtension};

use super::reported_php_extension;
use crate::proto::PhpExtensionAvailability;

/// A reading of `gd` with the given state.
fn reading(enabled: bool, availability: ExtensionAvailability) -> PhpExtension {
    PhpExtension {
        name: PhpExtensionName::parse("gd").unwrap(),
        enabled,
        availability,
    }
}

#[test]
fn the_name_and_the_loaded_state_travel_unchanged() {
    let on = reported_php_extension(&reading(true, ExtensionAvailability::Available));
    let off = reported_php_extension(&reading(false, ExtensionAvailability::Available));

    assert_eq!(on.name, "gd");
    assert!(on.enabled);
    assert!(!off.enabled);
}

#[test]
fn each_availability_maps_to_its_own_wire_value_and_never_to_unspecified() {
    for (domain, wire) in [
        (
            ExtensionAvailability::Available,
            PhpExtensionAvailability::Available,
        ),
        (
            ExtensionAvailability::DisableNotSupported,
            PhpExtensionAvailability::DisableNotSupported,
        ),
        (
            ExtensionAvailability::NotPackaged,
            PhpExtensionAvailability::NotPackaged,
        ),
    ] {
        let reported = reported_php_extension(&reading(true, domain));

        assert_eq!(reported.availability, wire as i32);
        assert_ne!(
            reported.availability,
            PhpExtensionAvailability::Unspecified as i32
        );
    }
}
