//! Tests for the `ban_address` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::{BanAddress, BanAddressError};

#[test]
fn v4_and_v6_addresses_parse_and_render_themselves() {
    for candidate in ["203.0.113.7", "10.0.0.1", "::1", "2001:db8::1"] {
        let address = BanAddress::parse_existing(candidate).unwrap();

        assert_eq!(address.to_string(), candidate);
    }

    for candidate in ["203.0.113.7", "10.0.0.1", "2001:db8::1"] {
        let address = BanAddress::parse(candidate).unwrap();

        assert_eq!(address.to_string(), candidate);
    }
}

#[test]
fn the_family_is_reported() {
    assert!(BanAddress::parse("203.0.113.7").unwrap().is_v4());
    assert!(!BanAddress::parse("2001:db8::1").unwrap().is_v4());

    assert_eq!(
        BanAddress::parse("203.0.113.7").unwrap().address(),
        "203.0.113.7".parse::<IpAddr>().unwrap()
    );
}

#[test]
fn a_network_is_refused_because_a_ban_is_one_address() {
    assert_eq!(
        BanAddress::parse("10.0.0.0/8"),
        Err(BanAddressError::Invalid {
            candidate: "10.0.0.0/8".to_owned(),
        })
    );
}

#[test]
fn a_hostname_and_a_scoped_address_are_refused() {
    for candidate in ["example.com", "fe80::1%eth0", "203.0.113.7 "] {
        assert_eq!(
            BanAddress::parse(candidate),
            Err(BanAddressError::Invalid {
                candidate: candidate.to_owned(),
            })
        );
    }
}

#[test]
fn a_second_spelling_of_the_same_address_is_refused_with_the_first_in_hand() {
    assert_eq!(
        BanAddress::parse("2001:0db8::1"),
        Err(BanAddressError::NotCanonical {
            canonical: "2001:db8::1".to_owned(),
        })
    );
    assert_eq!(
        BanAddress::parse("0:0:0:0:0:0:0:1"),
        Err(BanAddressError::NotCanonical {
            canonical: "::1".to_owned(),
        })
    );
}

#[test]
fn an_ipv4_address_in_ipv6_notation_is_refused() {
    // The same refusal `SourceCidr` makes, decided by the same predicate in
    // `validation::web::ipv4_disguise`. This test pins the wiring — that this
    // type asks the question at all, and reports it in its own error — while
    // `ipv4_disguise_tests` pins the answer itself.
    assert_eq!(
        BanAddress::parse("::ffff:1.2.3.4"),
        Err(BanAddressError::Ipv4InIpv6Notation {
            as_v4: "1.2.3.4".to_owned(),
        })
    );
    assert_eq!(
        BanAddress::parse("::102:304"),
        Err(BanAddressError::Ipv4InIpv6Notation {
            as_v4: "1.2.3.4".to_owned(),
        })
    );

    // `::` and `::1` share the v4-compatible shape and are ordinary IPv6
    // addresses, so this refusal is not the one they meet. `::1` is refused,
    // but as loopback and by the separate check the tests below pin.
    assert!(BanAddress::parse("::").is_ok());
    assert!(BanAddress::parse_existing("::1").is_ok());
}

#[test]
fn a_leading_zero_octet_is_refused() {
    assert_eq!(
        BanAddress::parse("010.0.0.1"),
        Err(BanAddressError::Invalid {
            candidate: "010.0.0.1".to_owned(),
        })
    );
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(BanAddress::parse(""), Err(BanAddressError::Empty));
}

#[test]
fn a_loopback_address_is_refused_because_such_a_ban_would_block_nothing() {
    // Both nftables tables the agent renders accept `iif "lo"` ahead of the
    // ban sets, so an element for a loopback address matches no packet: the
    // ban is installed, reported as placed, and does nothing. Refusing it here
    // — the last gate before the `nft` argument vector — is what stops the
    // panel journalling a ban that never happened.
    for candidate in ["127.0.0.1", "127.0.0.53", "127.255.255.254", "::1"] {
        assert_eq!(
            BanAddress::parse(candidate),
            Err(BanAddressError::Loopback {
                address: candidate.to_owned(),
            }),
            "{candidate} must be refused as loopback"
        );
    }
}

#[test]
fn an_ordinary_address_still_passes_the_loopback_check() {
    // The inverse control for the refusal above: a gate mutated to refuse
    // everything would pass every test that only ever hands it loopback. These
    // are the neighbours of the refused block on both families, including the
    // 128.0.0.1 that differs from 127.0.0.1 in one bit of the first octet.
    for candidate in [
        "126.255.255.255",
        "128.0.0.1",
        "203.0.113.7",
        "10.0.0.1",
        "::",
        "2001:db8::2",
        "2001:db8::1",
    ] {
        let address = BanAddress::parse(candidate).expect("a bannable address");

        assert_eq!(address.to_string(), candidate);
    }
}

#[test]
fn a_loopback_address_can_still_be_read_back_and_lifted() {
    // `parse_existing` is what listing and unbanning use. A host upgraded from
    // a version without the ban-side refusal can hold a loopback element the
    // old code placed, and refusing to read it would make the whole ban list
    // unreadable while refusing to lift it would make that element permanent.
    for candidate in ["127.0.0.1", "::1"] {
        let address = BanAddress::parse_existing(candidate).expect("a readable address");

        assert_eq!(address.to_string(), candidate);
    }
}

#[test]
fn a_scoped_loopback_address_is_refused_before_it_can_be_normalised_here() {
    // `::1%3` is what a scoped address looks like arriving from a panel that
    // has not stripped the scope; this type refuses the whole spelling as not
    // an address, and the panel's own normaliser is what turns it into `::1` —
    // which then meets the loopback refusal above. Both halves of that journey
    // end in a refusal, which is the property worth pinning.
    assert_eq!(
        BanAddress::parse("::1%3"),
        Err(BanAddressError::Invalid {
            candidate: "::1%3".to_owned(),
        })
    );
    assert_eq!(
        BanAddress::parse(
            "::1%3"
                .split('%')
                .next()
                .expect("splitting always yields a first part")
        ),
        Err(BanAddressError::Loopback {
            address: "::1".to_owned(),
        })
    );
}
