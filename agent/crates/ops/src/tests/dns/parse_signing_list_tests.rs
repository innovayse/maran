//! Reading which key has finished signing a zone out of `rndc signing -list`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_signing_list;
use crate::dns::model::key_publication::KeyPublication;

#[test]
fn a_finished_signing_names_the_key_that_made_it() {
    assert_eq!(
        parse_signing_list("Done signing with key 59409/ECDSAP256SHA256\n"),
        KeyPublication::Published(59_409)
    );
}

#[test]
fn a_wrong_tag_is_a_different_answer_from_the_right_one() {
    // The pair the panel compares against: the reading carries the tag as printed.
    assert_ne!(
        parse_signing_list("Done signing with key 59410/ECDSAP256SHA256\n"),
        KeyPublication::Published(59_409)
    );
}

#[test]
fn no_signing_record_is_not_published() {
    assert_eq!(
        parse_signing_list("No signing records found\n"),
        KeyPublication::NotPublished
    );
}

#[test]
fn a_signing_still_running_is_not_yet_published() {
    assert_eq!(
        parse_signing_list("Signing with key 59409/ECDSAP256SHA256\n"),
        KeyPublication::NotPublished
    );
}

#[test]
fn a_finished_key_wins_over_one_still_running() {
    let output =
        "Signing with key 11111/ECDSAP256SHA256\nDone signing with key 59409/ECDSAP256SHA256\n";

    assert_eq!(
        parse_signing_list(output),
        KeyPublication::Published(59_409)
    );
}

#[test]
fn two_different_finished_keys_are_not_a_reading_to_trust() {
    let output =
        "Done signing with key 1/ECDSAP256SHA256\nDone signing with key 2/ECDSAP256SHA256\n";

    assert_eq!(parse_signing_list(output), KeyPublication::NotObserved);
}

#[test]
fn the_same_key_twice_is_one_key() {
    let output =
        "Done signing with key 1/ECDSAP256SHA256\nDone signing with key 1/ECDSAP256SHA256\n";

    assert_eq!(parse_signing_list(output), KeyPublication::Published(1));
}

#[test]
fn one_line_that_is_not_understood_spoils_the_whole_reading() {
    let output = "Done signing with key 59409/ECDSAP256SHA256\nsomething else entirely\n";

    assert_eq!(parse_signing_list(output), KeyPublication::NotObserved);
}

#[test]
fn a_tag_that_is_not_a_number_in_range_is_not_understood() {
    for line in [
        "Done signing with key abc/ECDSAP256SHA256",
        "Done signing with key 70000/ECDSAP256SHA256",
        "Done signing with key -1/ECDSAP256SHA256",
        "Done signing with key 5",
        "Done signing with key /ECDSAP256SHA256",
        "Done signing with key 5/\u{1b}[2J",
    ] {
        assert_eq!(
            parse_signing_list(line),
            KeyPublication::NotObserved,
            "{line:?}"
        );
    }
}

#[test]
fn an_empty_answer_is_not_observed_and_never_not_published() {
    assert_eq!(parse_signing_list(""), KeyPublication::NotObserved);
}
