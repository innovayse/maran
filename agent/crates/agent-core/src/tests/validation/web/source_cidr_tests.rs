//! Tests for the `source_cidr` module.
//!
//! This value reaches a root-loaded nftables ruleset, and rules are compared as
//! text when deciding which one to delete — so the canonical-spelling checks
//! matter as much as the range checks.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::IpAddr;

use super::{MAX_IPV4_PREFIX, MAX_IPV6_PREFIX, SourceCidr, SourceCidrError};

#[test]
fn v4_and_v6_parse_and_canonicalise() {
    // Half the proposition: every canonical spelling parses and renders itself.
    for candidate in [
        "0.0.0.0/0",
        "10.0.0.0/8",
        "192.168.1.0/24",
        "203.0.113.7/32",
        "::/0",
        "2001:db8::/32",
        "::1/128",
    ] {
        let network = SourceCidr::parse(candidate).unwrap();

        assert_eq!(network.to_string(), candidate);
    }

    // The other half, and the one the name actually claims: a NON-canonical
    // spelling is refused, with the canonical one handed back. Without these,
    // deleting the canonicality enforcement outright would leave this test
    // green while its name still promised otherwise.
    for (candidate, canonical) in [
        ("2001:0db8::/32", "2001:db8::/32"),
        ("2001:DB8::/32", "2001:db8::/32"),
        ("10.0.0.0/08", "10.0.0.0/8"),
    ] {
        assert_eq!(
            SourceCidr::parse(candidate),
            Err(SourceCidrError::NotCanonical {
                canonical: canonical.to_owned(),
            })
        );
    }
}

#[test]
fn a_network_with_host_bits_set_is_refused_rather_than_masked() {
    // `203.0.113.7/24` is either one address or 256 of them, and the two differ
    // by a factor of 256 in what they let through. Masking would pick the wider
    // one on the caller's behalf; refusing makes them say which they meant, and
    // the error carries both spellings so saying so is one edit.
    assert_eq!(
        SourceCidr::parse("203.0.113.7/24"),
        Err(SourceCidrError::HostBitsSet {
            network: "203.0.113.0/24".to_owned(),
            host: "203.0.113.7/32".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("10.0.0.1/8"),
        Err(SourceCidrError::HostBitsSet {
            network: "10.0.0.0/8".to_owned(),
            host: "10.0.0.1/32".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("1.2.3.4/0"),
        Err(SourceCidrError::HostBitsSet {
            network: "0.0.0.0/0".to_owned(),
            host: "1.2.3.4/32".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("2001:db8::1/32"),
        Err(SourceCidrError::HostBitsSet {
            network: "2001:db8::/32".to_owned(),
            host: "2001:db8::1/128".to_owned(),
        })
    );

    // This is what makes the type's own claim true: without it `10.0.0.0/8` has
    // 2^24 accepted spellings, and "is this rule already present?" has 2^24
    // answers.
    assert!(SourceCidr::parse("10.0.0.0/8").is_ok());
    assert!(SourceCidr::parse("10.0.0.1/32").is_ok());
    assert!(SourceCidr::parse("::1/128").is_ok());
    assert!(SourceCidr::parse("::/0").is_ok());
}

#[test]
fn an_ipv4_address_in_ipv6_notation_is_refused() {
    // The firewall keeps one rule path per family and asks `is_v4()` which one
    // to use, so a v4 host arriving as a v6 address would be rendered into the
    // IPv6 rule and match nothing an IPv4 packet carries.
    assert_eq!(
        SourceCidr::parse("::ffff:1.2.3.4/128"),
        Err(SourceCidrError::Ipv4InIpv6Notation {
            as_v4: "1.2.3.4".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("::102:304/128"),
        Err(SourceCidrError::Ipv4InIpv6Notation {
            as_v4: "1.2.3.4".to_owned(),
        })
    );

    // `::` and `::1` share the v4-compatible shape and are ordinary IPv6
    // addresses every host uses, so they are the two exceptions.
    assert!(SourceCidr::parse("::/0").is_ok());
    assert!(SourceCidr::parse("::1/128").is_ok());
}

#[test]
fn the_parts_are_readable_and_the_family_is_reported() {
    let network = SourceCidr::parse("192.168.1.0/24").unwrap();

    assert_eq!(network.address(), "192.168.1.0".parse::<IpAddr>().unwrap());
    assert_eq!(network.prefix_length(), 24);
    assert!(network.is_v4());

    let network = SourceCidr::parse("2001:db8::/32").unwrap();

    assert_eq!(network.prefix_length(), 32);
    assert!(!network.is_v4());
}

#[test]
fn any_v4_is_the_unrestricted_source() {
    let network = SourceCidr::any_v4();

    assert_eq!(network.to_string(), "0.0.0.0/0");
    assert!(network.is_v4());
    assert_eq!(SourceCidr::parse("0.0.0.0/0").unwrap(), network);
}

#[test]
fn leading_zero_octets_are_refused() {
    // `010` is ambiguous between decimal ten and octal eight, and a firewall
    // rule that means one of two networks means neither.
    for candidate in ["010.0.0.1/32", "1.2.3.04/32"] {
        assert!(matches!(
            SourceCidr::parse(candidate),
            Err(SourceCidrError::InvalidAddress { .. })
        ));
    }
}

#[test]
fn a_second_spelling_of_the_same_network_is_refused_with_the_first_in_hand() {
    assert_eq!(
        SourceCidr::parse("2001:0db8::/32"),
        Err(SourceCidrError::NotCanonical {
            canonical: "2001:db8::/32".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("2001:DB8::/32"),
        Err(SourceCidrError::NotCanonical {
            canonical: "2001:db8::/32".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("0:0:0:0:0:0:0:1/128"),
        Err(SourceCidrError::NotCanonical {
            canonical: "::1/128".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("10.0.0.0/08"),
        Err(SourceCidrError::NotCanonical {
            canonical: "10.0.0.0/8".to_owned(),
        })
    );
}

#[test]
fn an_overlong_prefix_is_refused() {
    assert_eq!(
        SourceCidr::parse("1.2.3.4/33"),
        Err(SourceCidrError::PrefixTooLong {
            prefix: "33".to_owned(),
            maximum: MAX_IPV4_PREFIX,
        })
    );
    assert_eq!(
        SourceCidr::parse("2001:db8::/129"),
        Err(SourceCidrError::PrefixTooLong {
            prefix: "129".to_owned(),
            maximum: MAX_IPV6_PREFIX,
        })
    );
    assert_eq!(
        SourceCidr::parse("1.2.3.4/999"),
        Err(SourceCidrError::PrefixTooLong {
            prefix: "999".to_owned(),
            maximum: MAX_IPV4_PREFIX,
        })
    );
}

#[test]
fn the_longest_prefix_each_family_allows_is_accepted() {
    assert_eq!(
        SourceCidr::parse("1.2.3.4/32").unwrap().prefix_length(),
        MAX_IPV4_PREFIX
    );
    assert_eq!(
        SourceCidr::parse("::1/128").unwrap().prefix_length(),
        MAX_IPV6_PREFIX
    );
}

#[test]
fn a_v4_address_with_a_v6_sized_prefix_is_refused() {
    // /64 is an ordinary IPv6 network and a nonsense IPv4 one, so the bound is
    // per family rather than 128 for both.
    assert_eq!(
        SourceCidr::parse("10.0.0.0/64"),
        Err(SourceCidrError::PrefixTooLong {
            prefix: "64".to_owned(),
            maximum: MAX_IPV4_PREFIX,
        })
    );
}

#[test]
fn a_hostname_is_refused_without_dns() {
    // The agent resolves nothing: a rule built from a name would mean whatever
    // DNS said at the moment it was written.
    assert_eq!(
        SourceCidr::parse("example.com/32"),
        Err(SourceCidrError::InvalidAddress {
            address: "example.com".to_owned(),
        })
    );
    assert_eq!(
        SourceCidr::parse("localhost/32"),
        Err(SourceCidrError::InvalidAddress {
            address: "localhost".to_owned(),
        })
    );
}

#[test]
fn an_address_without_a_prefix_is_refused() {
    assert_eq!(
        SourceCidr::parse("10.0.0.1"),
        Err(SourceCidrError::MissingPrefix {
            candidate: "10.0.0.1".to_owned(),
        })
    );
}

#[test]
fn a_prefix_that_is_not_a_decimal_number_is_refused() {
    for prefix in ["", "+8", "8a", "0x8", "1234"] {
        assert_eq!(
            SourceCidr::parse(&format!("10.0.0.0/{prefix}")),
            Err(SourceCidrError::InvalidPrefix {
                prefix: prefix.to_owned(),
            })
        );
    }
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(SourceCidr::parse(""), Err(SourceCidrError::Empty));
}
