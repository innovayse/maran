//! How the database area's error holds a bound and tells it from the failures
//! it is easy to confuse it with.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::time::Duration;

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::output_stream::OutputStream;

use super::DbError;

/// A deadline failure, as the core builds one.
fn timed_out() -> BoundExceeded {
    BoundExceeded::TimedOut {
        program: String::from("/usr/bin/mariadb"),
        deadline: Duration::from_secs(120),
    }
}

#[test]
fn a_bound_converts_into_the_bound_variant_and_no_other() {
    assert_eq!(DbError::from(timed_out()), DbError::Bound(timed_out()));
}

#[test]
fn a_bound_names_the_client_and_says_the_statement_may_still_have_run() {
    let printed = DbError::Bound(timed_out()).to_string();

    assert!(printed.contains("/usr/bin/mariadb"), "{printed}");
    assert!(printed.contains("may still run"), "{printed}");
}

#[test]
fn an_output_past_its_cap_is_a_bound_naming_the_stream() {
    let exceeded = BoundExceeded::OutputTooLarge {
        program: String::from("/usr/bin/mariadb"),
        stream: OutputStream::Stderr,
        cap: 1024,
    };

    let printed = DbError::from(exceeded).to_string();

    assert!(printed.contains("1024"), "{printed}");
}

/// A bound is not any of the classifications a client's own exit can produce.
#[test]
fn a_bound_is_not_a_classified_client_failure() {
    let bound = DbError::Bound(timed_out());

    assert_ne!(bound, DbError::from_client(1, ""));
    assert_ne!(bound, DbError::client_killed(None));
    assert_ne!(bound, DbError::AccessDenied);
}

/// The inverse control: the client's own refusals still classify as before.
#[test]
fn a_clients_own_refusal_still_classifies_by_its_server_error_number() {
    assert_eq!(
        DbError::from_client(1, "ERROR 1045 (28000): Access denied"),
        DbError::AccessDenied
    );
    assert_eq!(
        DbError::from_client(1, "ERROR 1007 (HY000): exists"),
        DbError::AlreadyExists
    );
}

/// A client killed from outside carries no signal number, so it reads as zero.
#[test]
fn a_client_killed_from_outside_reports_signal_zero() {
    assert_eq!(
        DbError::client_killed(None),
        DbError::ClientKilled { signal: 0 }
    );
    assert_eq!(
        DbError::client_killed(Some(9)),
        DbError::ClientKilled { signal: 9 }
    );
}
