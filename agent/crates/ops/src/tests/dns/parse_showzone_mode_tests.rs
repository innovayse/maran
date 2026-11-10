//! Reading a zone's signing mode out of `rndc showzone`.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_templates::bind::zone_statement::ZoneStatement;

use super::parse_showzone_mode;
use crate::dns::model::signing_mode::SigningMode;

/// What `rndc showzone` prints around a statement.
fn shown(statement: &str) -> String {
    format!("zone \"example.com\" {statement}\n")
}

#[test]
fn the_statements_the_agent_renders_are_read_back_as_the_mode_they_were_rendered_for() {
    // The drift guard: the parser's policy names are the template's.
    let plain = ZoneStatement::unsigned("/z/example.com.db")
        .statement()
        .unwrap();
    let signed = ZoneStatement::signed("/s/example.com.db", "/k")
        .statement()
        .unwrap();
    let withdrawing = ZoneStatement::withdrawing("/s/example.com.db", "/k")
        .statement()
        .unwrap();

    assert_eq!(
        parse_showzone_mode(&shown(&plain)),
        Some(SigningMode::Unsigned)
    );
    assert_eq!(
        parse_showzone_mode(&shown(&signed)),
        Some(SigningMode::Signed)
    );
    assert_eq!(
        parse_showzone_mode(&shown(&withdrawing)),
        Some(SigningMode::Withdrawing)
    );
}

#[test]
fn a_policy_this_agent_did_not_write_is_not_understood() {
    let other = shown("{ type primary; file \"/z/example.com.db\"; dnssec-policy \"default\"; };");

    assert_eq!(parse_showzone_mode(&other), None);
}

#[test]
fn a_keyword_with_nothing_after_it_is_not_understood() {
    assert_eq!(
        parse_showzone_mode("zone \"example.com\" { dnssec-policy"),
        None
    );
}

#[test]
fn an_error_message_is_not_a_statement() {
    assert_eq!(
        parse_showzone_mode("rndc: 'showzone' failed: not found\n"),
        None
    );
    assert_eq!(parse_showzone_mode(""), None);
}

#[test]
fn a_statement_with_a_control_character_in_it_is_not_understood() {
    let hostile = shown("{ type primary; file \"/z/a\u{1b}[2J.db\"; };");

    assert_eq!(parse_showzone_mode(&hostile), None);
}

#[test]
fn a_policy_name_that_only_contains_ours_is_not_ours() {
    let lookalike = shown("{ type primary; dnssec-policy \"maran-csk-evil\"; };");

    assert_eq!(parse_showzone_mode(&lookalike), None);
}
