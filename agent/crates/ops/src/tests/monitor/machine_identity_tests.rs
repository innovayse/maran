//! Tests for `MachineIdentity::from_raw`: turning a host's raw,
//! unvalidated read into the typed present/absent answer.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::MachineIdentity;

#[test]
fn a_real_value_is_present_and_trimmed() {
    let identity = MachineIdentity::from_raw(Some("4e3ff4943c924fe4ab28141e8bebb6a3\n".to_owned()));

    assert_eq!(
        identity,
        MachineIdentity::Present("4e3ff4943c924fe4ab28141e8bebb6a3".to_owned())
    );
}

#[test]
fn a_missing_file_is_not_available() {
    let identity = MachineIdentity::from_raw(None);

    assert_eq!(identity, MachineIdentity::NotAvailable);
}

/// An empty file must never be reported as `Present(String::new())` — see the
/// type's own doc comment for why a fabricated empty value is exactly the
/// shape this type is built to prevent.
#[test]
fn an_empty_file_is_not_available_and_not_an_empty_present_value() {
    let identity = MachineIdentity::from_raw(Some(String::new()));

    assert_eq!(identity, MachineIdentity::NotAvailable);
}

#[test]
fn a_whitespace_only_file_is_not_available() {
    let identity = MachineIdentity::from_raw(Some("\n  \n".to_owned()));

    assert_eq!(identity, MachineIdentity::NotAvailable);
}
