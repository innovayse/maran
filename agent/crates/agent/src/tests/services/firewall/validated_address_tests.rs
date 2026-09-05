//! Tests for the check that revalidates the address an unban names.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_address;
use crate::proto::ErrorCode;

#[test]
fn an_ordinary_address_is_accepted_and_written_back_in_its_canonical_spelling() {
    for candidate in ["203.0.113.7", "2001:db8::1"] {
        let address = validated_address(candidate).expect("an unbannable address");

        assert_eq!(address.to_string(), candidate);
    }
}

#[test]
fn a_loopback_address_may_be_lifted_even_though_it_may_not_be_banned() {
    // The two directions differ on purpose. A host upgraded from a version
    // without the ban-side loopback refusal can still hold a loopback element
    // the old code placed, and refusing to lift it would make that element
    // permanent — refusing to add an inert ban is a protection, refusing to
    // clean one up is not.
    for candidate in ["127.0.0.1", "::1"] {
        let address = validated_address(candidate).expect("a loopback ban must be liftable");

        assert_eq!(address.to_string(), candidate);
    }
}

#[test]
fn anything_that_is_not_a_single_address_is_still_refused() {
    // The inverse control for the paragraph above: the unban path relaxes the
    // loopback rule and nothing else, so a network, a hostname and a value
    // carrying a newline all stay refused.
    for candidate in [
        "",
        "198.51.100.0/24",
        "attacker.example",
        "127.0.0.1\n",
        "::ffff:198.51.100.7",
    ] {
        let error =
            validated_address(candidate).expect_err("only a single address may be unbanned");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{candidate:?}");
    }
}
