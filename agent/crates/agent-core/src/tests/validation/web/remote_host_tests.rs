//! Tests for the `remote_host` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::net::{IpAddr, Ipv4Addr};

use super::{RemoteHost, RemoteHostError};

#[test]
fn a_dns_name_is_accepted_and_lowercased() {
    let host = RemoteHost::parse("Source.Example.COM").unwrap();

    assert_eq!(host.as_str(), "source.example.com");
    assert!(!host.is_literal_address());
    assert_eq!(host.literal_address(), None);
}

#[test]
fn a_single_label_name_on_the_operators_network_is_accepted() {
    assert_eq!(RemoteHost::parse("oldbox").unwrap().as_str(), "oldbox");
    assert_eq!(RemoteHost::parse("box.lan").unwrap().as_str(), "box.lan");
}

#[test]
fn a_public_ipv4_literal_is_accepted_as_a_literal() {
    let host = RemoteHost::parse("203.0.113.7").unwrap();

    assert!(host.is_literal_address());
    assert_eq!(
        host.literal_address(),
        Some(IpAddr::V4(Ipv4Addr::new(203, 0, 113, 7)))
    );
    assert_eq!(host.as_str(), "203.0.113.7");
}

#[test]
fn private_range_addresses_are_accepted_because_a_source_on_the_operators_network_is_ordinary() {
    for candidate in ["10.1.2.3", "192.168.0.10", "172.16.5.5", "fd00::5"] {
        assert!(
            RemoteHost::parse(candidate).unwrap().is_literal_address(),
            "{candidate}"
        );
    }
}

#[test]
fn an_ipv6_literal_is_kept_in_its_canonical_form() {
    let host = RemoteHost::parse("2001:0db8:0000:0000:0000:0000:0000:0001").unwrap();

    assert_eq!(host.as_str(), "2001:db8::1");
    assert!(host.is_literal_address());
}

#[test]
fn loopback_link_local_unspecified_and_multicast_literals_are_refused() {
    for candidate in [
        "127.0.0.1",
        "127.9.9.9",
        "169.254.169.254",
        "0.0.0.0",
        "0.1.2.3",
        "224.0.0.1",
        "239.255.255.250",
        "255.255.255.255",
        "::1",
        "::",
        "fe80::1",
        "febf::1",
        "ff02::1",
    ] {
        assert_eq!(
            RemoteHost::parse(candidate),
            Err(RemoteHostError::ForbiddenAddress),
            "{candidate}"
        );
    }
}

#[test]
fn an_ipv4_address_spelled_inside_an_ipv6_literal_is_judged_as_the_ipv4_one() {
    for candidate in ["::ffff:127.0.0.1", "::ffff:169.254.169.254", "::127.0.0.1"] {
        assert_eq!(
            RemoteHost::parse(candidate),
            Err(RemoteHostError::ForbiddenAddress),
            "{candidate}"
        );
    }
    // The inverse control: the same notation around an ordinary address is not
    // refused, so the refusal above is about the embedded address and not the form.
    assert!(RemoteHost::parse("::ffff:203.0.113.7").is_ok());
}

#[test]
fn an_address_just_outside_the_link_local_block_is_accepted() {
    assert!(RemoteHost::parse("fec0::1").is_ok());
    assert!(RemoteHost::parse("169.253.1.1").is_ok());
}

#[test]
fn the_localhost_family_is_refused() {
    for candidate in ["localhost", "LOCALHOST", "app.localhost"] {
        assert_eq!(
            RemoteHost::parse(candidate),
            Err(RemoteHostError::LocalName),
            "{candidate}"
        );
    }
    // A name merely containing the word is an ordinary name.
    assert!(RemoteHost::parse("localhost.example.com").is_ok());
    assert!(RemoteHost::parse("notlocalhost").is_ok());
}

#[test]
fn an_empty_candidate_is_refused() {
    assert_eq!(RemoteHost::parse(""), Err(RemoteHostError::Empty));
}

#[test]
fn text_outside_the_host_grammar_is_refused() {
    for candidate in [
        "ssh://host",
        "host/path",
        "host:22",
        "user@host",
        "-oProxyCommand=x",
        "-host",
        "host name",
        "host\n",
        "host\0",
        "[::1]",
        "fe80::1%eth0",
        "ho_st.example.com",
        "host..example.com",
        "host.",
        ".host",
        "bücher.example",
    ] {
        assert!(RemoteHost::parse(candidate).is_err(), "{candidate:?}");
    }
}

#[test]
fn malformed_and_shorthand_ipv4_literals_are_refused_as_not_a_host() {
    for candidate in ["999.1.1.1", "127.1", "1.2.3", "0177.0.0.1", "2130706433"] {
        assert_eq!(
            RemoteHost::parse(candidate),
            Err(RemoteHostError::NotAHost),
            "{candidate}"
        );
    }
}

#[test]
fn a_name_over_the_dns_limit_is_refused() {
    let label = "a".repeat(63);
    let long = format!("{label}.{label}.{label}.{label}.com");

    assert_eq!(RemoteHost::parse(&long), Err(RemoteHostError::NotAHost));
    assert!(RemoteHost::parse(&format!("{label}.com")).is_ok());
}

#[test]
fn the_error_never_carries_the_refused_text() {
    let message = RemoteHost::parse("127.0.0.1").unwrap_err().to_string();

    assert!(!message.contains("127.0.0.1"));
}
