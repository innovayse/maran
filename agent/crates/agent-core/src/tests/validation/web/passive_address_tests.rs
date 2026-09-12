//! Tests for the `passive_address` module.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md); the
//! source file declares this module with `#[path]`, keeping it a child able to
//! reach private items.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::web::passive_address_error::PassiveAddressError;

use super::PassiveAddress;

#[test]
fn a_dotted_quad_is_accepted_and_kept_exactly_as_it_was_typed() {
    // The inverse control: a validator that refused everything would satisfy
    // every refusal test below. This is the value an operator actually types.
    let address = PassiveAddress::parse("203.0.113.7").expect("a valid address");

    assert_eq!(address.as_str(), "203.0.113.7");
}

#[test]
fn the_two_edges_of_the_address_space_are_accepted() {
    // The bounds of the octet grammar, so a hand-rolled range check could not
    // creep in and shave an octet off either end unnoticed.
    assert_eq!(
        PassiveAddress::parse("0.0.0.0").expect("valid").as_str(),
        "0.0.0.0"
    );
    assert_eq!(
        PassiveAddress::parse("255.255.255.255")
            .expect("valid")
            .as_str(),
        "255.255.255.255"
    );
}

#[test]
fn an_address_carrying_a_newline_is_refused_with_the_whole_candidate_named() {
    // The reason this type exists. `vsftpd.conf` is a line-oriented
    // `key=value` file read by a daemon that starts as root, so one embedded
    // newline appends a directive of the caller's choosing — and
    // `chroot_local_user=NO` alone turns every FTPS login into a session over
    // the whole filesystem (rules/security.md §4).
    let result = PassiveAddress::parse("203.0.113.7\npasv_min_port=1");

    assert_eq!(
        result,
        Err(PassiveAddressError::Invalid {
            candidate: "203.0.113.7\npasv_min_port=1".to_owned()
        })
    );
}

#[test]
fn a_carriage_return_is_refused_too_because_the_file_is_read_line_by_line() {
    assert!(matches!(
        PassiveAddress::parse("203.0.113.7\r"),
        Err(PassiveAddressError::Invalid { .. })
    ));
}

#[test]
fn a_host_name_is_refused_rather_than_resolved() {
    // Resolving here would make what the daemon advertises depend on a
    // resolver, and on WHEN it was asked.
    assert_eq!(
        PassiveAddress::parse("ftp.example.com"),
        Err(PassiveAddressError::Invalid {
            candidate: "ftp.example.com".to_owned()
        })
    );
}

#[test]
fn a_leading_zero_octet_is_refused_because_it_names_two_different_hosts() {
    // `010.0.0.1` is decimal ten to some readers and octal eight to others.
    // The refusal comes from `Ipv4Addr`'s own grammar; this test is what would
    // go red if that grammar were ever relaxed, instead of the panel silently
    // starting to write an ambiguous address into a root daemon's config.
    assert!(matches!(
        PassiveAddress::parse("010.0.0.1"),
        Err(PassiveAddressError::Invalid { .. })
    ));
    assert!(matches!(
        PassiveAddress::parse("203.0.113.07"),
        Err(PassiveAddressError::Invalid { .. })
    ));
}

#[test]
fn whitespace_at_either_end_is_refused_rather_than_trimmed() {
    // Trimming would accept two spellings of one value, and the one written to
    // the file would not be the one the operator can see they typed.
    for candidate in [" 203.0.113.7", "203.0.113.7 ", "\t203.0.113.7"] {
        assert!(
            matches!(
                PassiveAddress::parse(candidate),
                Err(PassiveAddressError::Invalid { .. })
            ),
            "{candidate:?} must be refused"
        );
    }
}

#[test]
fn a_quad_that_is_not_four_octets_is_refused_at_either_end_of_the_count() {
    for candidate in ["203.0.113", "203.0.113.7.9", "203", ".", "..."] {
        assert!(
            matches!(
                PassiveAddress::parse(candidate),
                Err(PassiveAddressError::Invalid { .. })
            ),
            "{candidate:?} must be refused"
        );
    }
}

#[test]
fn an_octet_past_255_is_refused() {
    assert!(matches!(
        PassiveAddress::parse("203.0.113.256"),
        Err(PassiveAddressError::Invalid { .. })
    ));
}

#[test]
fn an_empty_address_is_refused_as_empty_and_not_as_invalid() {
    // Its own variant, because "you typed nothing" and "what you typed is not
    // an address" are different things to tell an operator.
    assert_eq!(PassiveAddress::parse(""), Err(PassiveAddressError::Empty));
}

#[test]
fn an_ipv6_address_is_refused_as_the_wrong_family_and_named_as_such() {
    // `pasv_address` has no IPv6 form — the PASV reply carries four decimal
    // octets — so an operator who typed one is told THAT rather than being told
    // their address is not an address.
    for candidate in ["::1", "2001:db8::1", "::ffff:203.0.113.7"] {
        assert_eq!(
            PassiveAddress::parse(candidate),
            Err(PassiveAddressError::NotIpv4 {
                candidate: candidate.to_owned()
            }),
            "{candidate:?} must be refused as the wrong family"
        );
    }
}

#[test]
fn every_accepted_address_round_trips_through_its_own_text() {
    // What lets the type store text rather than an `Ipv4Addr`: the accepted set
    // is exactly the set whose canonical rendering is the input. A standard
    // library that started accepting a second spelling of one address would
    // fail here.
    for candidate in [
        "203.0.113.7",
        "10.0.0.1",
        "192.168.1.254",
        "0.0.0.0",
        "255.255.255.255",
    ] {
        let address = PassiveAddress::parse(candidate).expect("a valid address");

        assert_eq!(address.as_str(), candidate);
        assert_eq!(
            PassiveAddress::parse(address.as_str()).expect("re-parses"),
            address
        );
    }
}
