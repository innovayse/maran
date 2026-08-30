//! Tests for the `adapter_for` module.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside the
//! unit they exercise, the same separation the backend uses (rules/testing.md).
//! `adapter_for.rs` declares this file with `#[path]`, which keeps it a child module and
//! therefore able to reach private items — a crate-level `tests/` directory sees
//! only the public API and could not test them at all.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// `DistroAdapter` is deliberately not imported: `adapter_for` hands back a
// `dyn DistroAdapter`, and calling a method on a trait object does not need the
// trait in scope — importing it here was an unused import that failed the build.
use super::{DistroFamily, adapter_for};

#[test]
fn every_family_gets_the_adapter_it_asked_for() {
    for family in [DistroFamily::Debian, DistroFamily::Rhel] {
        assert_eq!(adapter_for(family).family(), family);
    }
}
