//! Byte-for-byte goldens for the zone statement `rndc addzone` and `rndc modzone`
//! are given, and the guards that hold what a golden cannot.
//!
//! The golden diff is the review artifact: a reviewer reads exactly what the
//! daemon is told about where a zone lives and whether it signs it
//! (rules/testing.md).
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::bind::zone_statement::ZoneStatement;

/// Reads a golden by the name of the render type that produces it.
fn golden(relative: &str) -> String {
    std::fs::read_to_string(format!("tests/golden/{relative}"))
        .unwrap_or_else(|error| panic!("golden {relative} is missing: {error}"))
}

/// The plain zone file the Debian family's agent writes.
const PLAIN_FILE: &str = "/var/lib/bind/maran/zones/example.com.db";
/// The signed zone file of the same zone.
const SIGNED_FILE: &str = "/var/lib/bind/maran/signed-zones/example.com.db";
/// The key directory.
const KEY_DIRECTORY: &str = "/var/lib/bind/maran/keys";

/// The unsigned statement is its golden, byte for byte.
#[test]
fn the_unsigned_statement_renders_its_golden() {
    assert_eq!(
        ZoneStatement::unsigned(PLAIN_FILE).render_config().unwrap(),
        golden("bind/zone_statement_unsigned.conf")
    );
}

/// The signed statement is its golden, byte for byte.
#[test]
fn the_signed_statement_renders_its_golden() {
    assert_eq!(
        ZoneStatement::signed(SIGNED_FILE, KEY_DIRECTORY)
            .render_config()
            .unwrap(),
        golden("bind/zone_statement_signed.conf")
    );
}

/// The withdrawing statement is its golden, byte for byte.
#[test]
fn the_withdrawing_statement_renders_its_golden() {
    assert_eq!(
        ZoneStatement::withdrawing(SIGNED_FILE, KEY_DIRECTORY)
            .render_config()
            .unwrap(),
        golden("bind/zone_statement_withdrawing.conf")
    );
}

/// The control: the unsigned statement an argv receives is exactly what the
/// agent built with `format!` before this type existed. The old literal is
/// restated here on purpose, as it was in `ops/src/dns/put_zone.rs`.
#[test]
fn the_unsigned_statement_equals_the_text_the_agent_used_to_format() {
    let old = format!("{{ type primary; file \"{PLAIN_FILE}\"; }};");

    assert_eq!(
        ZoneStatement::unsigned(PLAIN_FILE).statement().unwrap(),
        old
    );
}

/// The argv form is the render without its one final newline, for every shape,
/// and the render keeps it (the census every golden answers to).
#[test]
fn the_statement_is_the_render_without_its_final_newline() {
    for statement in [
        ZoneStatement::unsigned(PLAIN_FILE),
        ZoneStatement::signed(SIGNED_FILE, KEY_DIRECTORY),
        ZoneStatement::withdrawing(SIGNED_FILE, KEY_DIRECTORY),
    ] {
        let rendered = statement.render_config().unwrap();
        let argument = statement.statement().unwrap();

        assert!(rendered.ends_with("};\n"));
        assert!(argument.ends_with("};"));
        assert_eq!(format!("{argument}\n"), rendered);
        assert_eq!(rendered.matches('\n').count(), 1);
    }
}

/// Every input reaches the text, under a spelling no golden shares: a path
/// written as a literal in the template would not follow.
#[test]
fn every_path_reaches_the_rendered_statement() {
    let signed = ZoneStatement::signed("/sentinel/signed/z.db", "/sentinel/keys")
        .statement()
        .unwrap();
    let unsigned = ZoneStatement::unsigned("/sentinel/plain/z.db")
        .statement()
        .unwrap();

    assert!(signed.contains("file \"/sentinel/signed/z.db\";"));
    assert!(signed.contains("key-directory \"/sentinel/keys\";"));
    assert!(unsigned.contains("file \"/sentinel/plain/z.db\";"));
}

/// An unsigned zone is told nothing about keys or a policy; a signed one is told
/// all three clauses, and a withdrawing one the other policy and nothing else
/// different.
#[test]
fn the_signing_clauses_appear_exactly_when_the_zone_is_signed() {
    let unsigned = ZoneStatement::unsigned(PLAIN_FILE).statement().unwrap();
    let signed = ZoneStatement::signed(SIGNED_FILE, KEY_DIRECTORY)
        .statement()
        .unwrap();
    let withdrawing = ZoneStatement::withdrawing(SIGNED_FILE, KEY_DIRECTORY)
        .statement()
        .unwrap();

    for clause in ["key-directory", "dnssec-policy", "inline-signing yes;"] {
        assert!(!unsigned.contains(clause), "unsigned states {clause}");
        assert!(signed.contains(clause), "signed lacks {clause}");
        assert!(withdrawing.contains(clause), "withdrawing lacks {clause}");
    }
    assert!(signed.contains("dnssec-policy \"maran-csk\";"));
    assert!(withdrawing.contains("dnssec-policy \"insecure\";"));
    assert!(!signed.contains("insecure"));
    assert!(!withdrawing.contains("maran-csk"));
    assert_eq!(
        signed.replace("maran-csk", "insecure"),
        withdrawing,
        "the withdrawing statement differs from the signed one by the policy only"
    );
}

/// A signed statement without inline signing, which the daemon would refuse (a
/// policy needs its signed copy kept apart from the zone file), is not what the
/// type renders: the planted wrong statement is not equal to the golden.
#[test]
fn a_signed_statement_without_inline_signing_is_not_the_golden() {
    let planted = golden("bind/zone_statement_signed.conf").replace(" inline-signing yes;", "");

    assert_ne!(
        ZoneStatement::signed(SIGNED_FILE, KEY_DIRECTORY)
            .render_config()
            .unwrap(),
        planted
    );
    assert!(!planted.contains("inline-signing"));
}
