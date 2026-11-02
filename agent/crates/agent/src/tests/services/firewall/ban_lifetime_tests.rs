//! Tests for the reading of a ban request's term.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use super::ban_lifetime;
use crate::proto::ErrorCode;
use crate::proto::PermanentBan;
use crate::proto::ban_address_request::Term;

#[test]
fn a_permanent_term_is_a_ban_with_no_timeout() {
    let lifetime = ban_lifetime(Some(&Term::Permanent(PermanentBan {})), 0).unwrap();

    assert_eq!(lifetime, None);
}

#[test]
fn a_permanent_term_wins_over_a_stale_duration() {
    // A request that carries `term` is decided by `term` alone.
    let lifetime = ban_lifetime(Some(&Term::Permanent(PermanentBan {})), 900).unwrap();

    assert_eq!(lifetime, None);
}

#[test]
fn a_timed_term_is_exactly_that_many_seconds() {
    let lifetime = ban_lifetime(Some(&Term::ExpiresAfterSeconds(900)), 0).unwrap();

    assert_eq!(lifetime, Some(Duration::from_secs(900)));
}

#[test]
fn a_timed_term_of_zero_seconds_is_refused_rather_than_read_as_permanent() {
    let error = ban_lifetime(Some(&Term::ExpiresAfterSeconds(0)), 900)
        .expect_err("zero seconds is not a lifetime");

    assert_eq!(error.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn without_a_term_a_duration_of_zero_is_a_permanent_ban() {
    assert_eq!(ban_lifetime(None, 0).unwrap(), None);
}

#[test]
fn without_a_term_the_deprecated_duration_still_sets_the_lifetime() {
    assert_eq!(
        ban_lifetime(None, 60).unwrap(),
        Some(Duration::from_secs(60))
    );
}
