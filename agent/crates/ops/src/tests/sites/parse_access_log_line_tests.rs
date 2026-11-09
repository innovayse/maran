//! Tests for the access-log tokenizer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::parse_access_log_line;
use crate::sites::fake_site_log_host::access_line;

fn parse(line: &str) -> Option<crate::sites::model::parsed_access_line::ParsedAccessLine> {
    parse_access_log_line(line.trim_end_matches('\n').as_bytes())
}

fn path_of(target: &str) -> Option<String> {
    parse(&access_line("07/Nov/2026:10:12:13 +0000", 200, 1, target))
        .unwrap()
        .path
}

#[test]
fn a_combined_line_gives_its_day_status_bytes_and_path() {
    let parsed = parse(&access_line(
        "07/Nov/2026:10:12:13 +0000",
        404,
        512,
        "/index.html",
    ))
    .unwrap();

    assert_eq!(parsed.date.to_iso(), "2026-11-07");
    assert_eq!(parsed.status, 404);
    assert_eq!(parsed.body_bytes, 512);
    assert_eq!(parsed.path.as_deref(), Some("/index.html"));
}

#[test]
fn the_query_string_is_cut_before_the_path_is_kept() {
    let kept = path_of("/reset?token=hunter2&email=a@b.c").unwrap();

    assert_eq!(kept, "/reset");
    assert!(!kept.contains("hunter2"));
}

#[test]
fn the_day_is_the_utc_day_whatever_zone_the_line_was_written_in() {
    let east = parse(&access_line("08/Nov/2026:00:30:00 +0300", 200, 1, "/")).unwrap();
    let west = parse(&access_line("07/Nov/2026:23:30:00 -0500", 200, 1, "/")).unwrap();

    assert_eq!(east.date.to_iso(), "2026-11-07");
    assert_eq!(west.date.to_iso(), "2026-11-08");
}

#[test]
fn a_dash_for_body_bytes_is_zero() {
    let line = "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 304 - \"-\" \"x\"";

    assert_eq!(parse(line).unwrap().body_bytes, 0);
}

#[test]
fn a_remote_user_does_not_change_what_is_read() {
    let line =
        "203.0.113.9 - alice [07/Nov/2026:10:12:13 +0000] \"GET /a HTTP/1.1\" 200 5 \"-\" \"x\"";

    assert_eq!(parse(line).unwrap().path.as_deref(), Some("/a"));
}

#[test]
fn a_request_with_no_usable_path_still_counts_but_has_no_path() {
    let dash = "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"-\" 400 0 \"-\" \"-\"";

    assert_eq!(parse(dash).unwrap().path, None);
    assert_eq!(parse(dash).unwrap().status, 400);
    assert_eq!(path_of("http://other.example/x"), None);
    assert_eq!(path_of("*"), None);
}

#[test]
fn a_path_is_kept_up_to_the_cap_and_not_beyond_it() {
    let at_cap = format!("/{}", "a".repeat(511));
    let over_cap = format!("/{}", "a".repeat(512));

    assert_eq!(path_of(&at_cap).as_deref(), Some(at_cap.as_str()));
    assert_eq!(path_of(&over_cap), None);
}

#[test]
fn a_control_character_that_survives_in_a_path_is_replaced() {
    let line = "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET /a\u{1}b\u{7f}c HTTP/1.1\" 200 5 \"-\" \"x\"";

    assert_eq!(parse(line).unwrap().path.as_deref(), Some("/a_b_c"));
}

#[test]
fn a_path_that_is_not_utf8_is_stored_as_text() {
    let mut line = b"203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET /a".to_vec();
    line.extend_from_slice(&[0xff, 0xfe]);
    line.extend_from_slice(b" HTTP/1.1\" 200 5 \"-\" \"x\"");

    let parsed = parse_access_log_line(&line).unwrap();
    assert!(parsed.path.unwrap().starts_with("/a"));
}

#[test]
fn lines_that_are_not_the_combined_format_are_refused() {
    for refused in [
        "",
        "garbage",
        "203.0.113.9",
        "203.0.113.9 - - no bracket \"GET / HTTP/1.1\" 200 5",
        "203.0.113.9 - - [07/Xxx/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 200 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:24:12:13 +0000] \"GET / HTTP/1.1\" 200 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +9999] \"GET / HTTP/1.1\" 200 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 0000] \"GET / HTTP/1.1\" 200 5 \"-\" \"x\"",
        "203.0.113.9 - - [32/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 200 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] GET / 200 5",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1 200 5",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 099 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 600 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 2x0 5 \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 200 abc \"-\" \"x\"",
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET / HTTP/1.1\" 200 99999999999999999999 \"-\" \"x\"",
    ] {
        assert!(parse(refused).is_none(), "{refused:?}");
    }
}

#[test]
fn a_line_over_the_cap_is_refused_even_when_it_is_well_formed() {
    let padding = "a".repeat(9_000);
    let line = format!(
        "203.0.113.9 - - [07/Nov/2026:10:12:13 +0000] \"GET /x HTTP/1.1\" 200 5 \"-\" \"{padding}\""
    );

    assert!(line.len() > 8 * 1024);
    assert!(parse(&line).is_none());
}

#[test]
fn every_prefix_of_a_valid_line_is_answered_without_a_panic() {
    let line = access_line("07/Nov/2026:10:12:13 +0000", 200, 5, "/x?y=1");

    for end in 0..line.len() {
        let _ = parse_access_log_line(&line.as_bytes()[..end]);
    }
}
