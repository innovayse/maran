//! Tests for how the config-write protocol's and the PHP area's failures fold
//! into the site area's.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints").
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::SitesOpError;
use crate::php::PhpOpError;
use crate::safe_write::SafeWriteError;
use crate::safe_write::adapter_programs::timed_out;

/// A bound from the protocol arrives as a bound.
#[test]
fn a_bound_from_the_protocol_reaches_the_sites_area_as_a_bound() {
    // `NginxValidation` would send an operator to edit a vhost that nginx never read,
    // and `ConfigWrite` would say the write failed when the host did not answer.
    let error = SitesOpError::from(SafeWriteError::Bound(timed_out("/usr/sbin/nginx")));

    match error {
        SitesOpError::Bound(bound) => assert_eq!(bound, timed_out("/usr/sbin/nginx")),
        other => panic!("expected Bound, got {other:?}"),
    }
}

/// The inverse control: a refusal keeps its own variant and its tool output.
#[test]
fn a_refused_validation_still_reaches_the_sites_area_as_nginx_validation() {
    let error = SitesOpError::from(SafeWriteError::ValidationFailed {
        stderr: "unknown directive".to_owned(),
    });

    assert!(
        matches!(error, SitesOpError::NginxValidation { stderr } if stderr == "unknown directive")
    );
}

/// A bound the PHP area reports stays a bound when a site operation calls it.
#[test]
fn a_bound_from_the_php_area_reaches_the_sites_area_as_a_bound() {
    let error = SitesOpError::from(PhpOpError::Bound(timed_out("/usr/bin/systemctl")));

    assert!(matches!(error, SitesOpError::Bound(_)), "{error:?}");
}
