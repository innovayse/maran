//! Tests for the check that turns a ban request into an address and a lifetime.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use super::validated_ban;
use crate::proto::ErrorCode;

#[test]
fn a_duration_of_zero_is_a_ban_with_no_timeout_rather_than_one_expiring_now() {
    // `firewall.proto` states it: 0 means permanent until explicitly unbanned.
    // `None` reaches `ban_address` as "add no timeout clause"; a zero `Duration`
    // would render `timeout 0s`, which nft reads the same way but which says it
    // in a form that reads like a mistake.
    let (address, lifetime) = validated_ban("198.51.100.7", 0).expect("a permanent ban is valid");

    assert_eq!(address.to_string(), "198.51.100.7");
    assert_eq!(lifetime, None);
}

#[test]
fn a_duration_becomes_a_lifetime_of_exactly_that_many_seconds() {
    let (_, lifetime) = validated_ban("198.51.100.7", 900).expect("a timed ban is valid");

    assert_eq!(lifetime, Some(Duration::from_secs(900)));
}

#[test]
fn an_ipv6_address_is_accepted_and_decides_which_set_the_element_joins() {
    // The address goes on to be an argument of an `nft` invocation that runs as
    // root, and it decides WHICH of the two typed sets the element joins — the
    // sets are `ipv4_addr` and `ipv6_addr`, so a family read wrongly means an
    // add that nft refuses outright.
    let (address, _) =
        validated_ban("2001:db8::1", 60).expect("an ipv6 address is a valid ban target");

    assert_eq!(address.to_string(), "2001:db8::1");
    assert!(!address.is_v4());
}

#[test]
fn an_address_spelled_some_other_way_than_the_canonical_one_is_refused() {
    // Not merely tidiness: an unban has to name the same string the ban did,
    // and a type that accepted two spellings of one address would let a panel
    // record a ban it can never lift. The refusal carries the canonical form,
    // so the caller has the value to retry with.
    let error = validated_ban("2001:0DB8:0000:0000:0000:0000:0000:0001", 60)
        .expect_err("a non-canonical spelling must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(
        error.message.contains("2001:db8::1"),
        "the refusal must hand back the spelling to retry with: {}",
        error.message
    );
}

#[test]
fn an_ipv4_address_in_ipv6_clothing_is_refused_rather_than_banned_in_the_wrong_set() {
    // `::ffff:198.51.100.7` names an IPv4 host. Added to the IPv6 set it would
    // drop nothing at all, and the panel would record an attacker as blocked.
    let error = validated_ban("::ffff:198.51.100.7", 60)
        .expect_err("an ipv4 address in ipv6 notation must be refused");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    assert!(
        error.message.contains("198.51.100.7"),
        "the refusal must hand back the address to retry with: {}",
        error.message
    );
}

#[test]
fn anything_that_is_not_a_single_address_is_refused() {
    // A network is not a ban target, a hostname is not an address, and a value
    // with a newline in it is an attempt to append a command of somebody else's
    // choosing to an argument vector run as root.
    for candidate in [
        "",
        "198.51.100.0/24",
        "attacker.example",
        "198.51.100.7 drop",
        "198.51.100.7\n",
    ] {
        let error = validated_ban(candidate, 60).expect_err("only a single address may be banned");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{candidate:?}");
    }
}

#[test]
fn a_loopback_address_is_refused_rather_than_banned_where_nothing_would_match_it() {
    // The ban path and the unban path deliberately differ here: a ban on
    // loopback is inert, because both nftables tables accept `iif "lo"` before
    // either ban set is consulted, so the agent declines to install one and
    // the panel journals a ban that did not happen instead of one that did.
    for candidate in ["127.0.0.1", "127.0.0.53", "::1"] {
        let error =
            validated_ban(candidate, 60).expect_err("a loopback ban must be refused at the agent");

        assert_eq!(error.code, ErrorCode::InvalidInput as i32, "{candidate:?}");
        assert!(
            error.message.contains("loopback"),
            "the refusal must say why: {}",
            error.message
        );
    }
}

#[test]
fn an_ordinary_address_next_to_the_loopback_block_is_still_banned() {
    // The inverse control: a check that refused everything would satisfy the
    // test above on its own. `128.0.0.1` is one bit away from `127.0.0.1`.
    for candidate in ["126.255.255.255", "128.0.0.1", "203.0.113.7", "2001:db8::2"] {
        let (address, _) = validated_ban(candidate, 60).expect("an ordinary address is bannable");

        assert_eq!(address.to_string(), candidate);
    }
}
