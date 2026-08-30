//! Tests for the `peer_policy` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `peer_policy.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::PeerPolicy;

/// The uid the panel runs as in these tests; any non-zero value would do.
const PANEL_UID: u32 = 1000;

#[test]
fn configured_uid_is_permitted() {
    assert!(PeerPolicy::new(PANEL_UID).permits(PANEL_UID));
}

#[test]
fn root_is_not_permitted_when_another_uid_is_configured() {
    assert!(!PeerPolicy::new(PANEL_UID).permits(0));
}

#[test]
fn unrelated_uid_is_not_permitted() {
    assert!(!PeerPolicy::new(PANEL_UID).permits(PANEL_UID + 1));
}
