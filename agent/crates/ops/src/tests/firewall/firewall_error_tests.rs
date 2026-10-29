//! How the firewall area's error holds a bound and tells it from the `nft`
//! failures it is easy to confuse it with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;

use super::FirewallError;

/// A deadline failure, as the core builds one.
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: String::from("/usr/sbin/nft"),
        deadline: Duration::from_secs(120),
    }
}

#[test]
fn a_bound_converts_into_the_bound_variant_and_no_other() {
    assert_eq!(
        FirewallError::from(timed_out()),
        FirewallError::Bound(timed_out())
    );
}

#[test]
fn a_bound_names_nft_and_says_the_kill_is_not_a_rollback() {
    let printed = FirewallError::Bound(timed_out()).to_string();

    assert!(printed.contains("/usr/sbin/nft"), "{printed}");
    assert!(printed.contains("may still run"), "{printed}");
}

/// A bound is not a refusal of the ruleset and not an `nft` that failed.
#[test]
fn a_bound_is_neither_a_refusal_nor_an_nft_failure() {
    let bound = FirewallError::Bound(timed_out());

    assert_ne!(
        bound,
        FirewallError::NftFailed {
            stderr: String::new()
        }
    );
    assert!(!matches!(bound, FirewallError::RuleRefusedByNft { .. }));
}
