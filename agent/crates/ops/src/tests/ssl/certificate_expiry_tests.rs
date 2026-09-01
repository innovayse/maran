//! Reading `notAfter`, and refusing everything that is not it.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sites::fake_site_host::distro;
use crate::ssl::certificate_expiry::{certificate_expiry, parse_end_date};
use crate::ssl::fake_ssl_host::{CERTIFICATE_PEM, EXPIRY_UNIX, FakeSslHost, matching_material};
use crate::ssl::ssl_op_error::SslOpError;

#[test]
fn the_fixture_certificates_expiry_is_read_as_unix_seconds() {
    let host = FakeSslHost::passing();

    let expiry = certificate_expiry(&host, distro(), &matching_material()).unwrap();

    assert_eq!(expiry, EXPIRY_UNIX);
}

#[test]
fn a_day_openssl_padded_to_two_columns_is_parsed() {
    // `Feb  9`, with two spaces. A split on a single space reads the day as an
    // empty field and the whole date silently fails — or worse, shifts.
    assert_eq!(
        parse_end_date("notAfter=Feb  9 20:14:26 2095 GMT"),
        Some(EXPIRY_UNIX)
    );
}

#[test]
fn a_two_digit_day_is_parsed_the_same_way() {
    assert_eq!(
        parse_end_date("notAfter=Jan 1 00:00:00 1970 GMT"),
        Some(0),
        "the epoch is the one answer that proves the calendar arithmetic itself"
    );
    assert_eq!(
        parse_end_date("notAfter=Mar  1 00:00:00 2024 GMT"),
        Some(1_709_251_200),
        "the day after a leap day, which is where an off-by-one lands"
    );
}

#[test]
fn a_leap_day_is_a_real_date_and_the_same_day_in_a_common_year_is_not() {
    assert!(parse_end_date("notAfter=Feb 29 00:00:00 2024 GMT").is_some());
    assert_eq!(parse_end_date("notAfter=Feb 29 00:00:00 2023 GMT"), None);
    // 2100 is divisible by 4 and is not a leap year, which is the rule a
    // hand-written check forgets.
    assert_eq!(parse_end_date("notAfter=Feb 29 00:00:00 2100 GMT"), None);
}

#[test]
fn a_date_in_any_other_shape_is_refused_rather_than_guessed_at() {
    for printed in [
        "notAfter=Feb 9 20:14:26 2095 CET",
        "notAfter=Smarch 9 20:14:26 2095 GMT",
        "notAfter=Feb 9 20:14:26 GMT",
        "notAfter=Feb 9 25:14:26 2095 GMT",
        "notAfter=Feb 31 20:14:26 2095 GMT",
        "notBefore=Feb 9 20:14:26 2095 GMT",
        "",
    ] {
        assert_eq!(
            parse_end_date(printed),
            None,
            "`{printed}` must not become a timestamp: a guessed expiry is a site \
             that silently stops working on a day nobody has in a calendar"
        );
    }
}

#[test]
fn an_unparseable_date_is_an_error_and_not_a_default() {
    let host = FakeSslHost::passing();
    host.set_end_date("notAfter=whenever\n");

    let failure = certificate_expiry(&host, distro(), &matching_material()).unwrap_err();

    assert!(matches!(failure, SslOpError::ExpiryUnreadable { .. }));
}

#[test]
fn a_certificate_openssl_refuses_is_reported_with_the_tools_own_words() {
    let host = FakeSslHost::passing();
    host.refuse("x509", "unable to load certificate");

    let failure = certificate_expiry(&host, distro(), &matching_material()).unwrap_err();

    match failure {
        SslOpError::MalformedCertificate { reason } => {
            assert_eq!(reason, "unable to load certificate");
        }
        other => panic!("expected a malformed certificate, got {other:?}"),
    }
}

/// The openssl this test asks about the committed certificate.
const OPENSSL: &str = "/usr/bin/openssl";

#[test]
#[ignore = "spawns the host's own openssl; run with --ignored to check the fixture"]
fn the_committed_fixture_really_does_expire_when_the_constant_says() {
    // EXPIRY_UNIX and the committed certificate can drift apart silently — the
    // rest of the suite compares the constant against a canned string derived
    // from the same constant, so it would agree with itself forever. This is the
    // one test that asks the real tool about the real bytes. Ignored by default
    // because a build container is not obliged to ship openssl.
    //
    // Asked to run, it REFUSES rather than skips when openssl is absent. This is
    // the only ignored test in the workspace that is not a polygon test, and it
    // used to pass quietly wherever `/usr/bin/openssl` happened to exist and
    // panic on an unwrap where it did not — so "it ran" and "it was skipped"
    // looked identical in the one place the distinction matters
    // (rules/testing.md: "no tests found" is a failure, never a pass).
    assert!(
        std::path::Path::new(OPENSSL).exists(),
        "this test asks the real openssl about the committed fixture; {OPENSSL} is \
         not on this host, so it cannot run — install openssl or run it in a polygon \
         container, but do not read its absence as a pass"
    );

    let printed = std::process::Command::new(OPENSSL)
        .args(["x509", "-noout", "-enddate"])
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .spawn()
        .and_then(|mut child| {
            use std::io::Write as _;
            child
                .stdin
                .take()
                .unwrap()
                .write_all(CERTIFICATE_PEM.as_bytes())?;
            child.wait_with_output()
        })
        .unwrap();

    let printed = String::from_utf8_lossy(&printed.stdout);
    assert_eq!(parse_end_date(printed.trim()), Some(EXPIRY_UNIX));
}
