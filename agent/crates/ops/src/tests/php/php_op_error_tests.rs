//! Tests for how the config-write protocol's failures fold into the PHP area's.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints").
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::PhpOpError;
use crate::safe_write::SafeWriteError;
use crate::safe_write::adapter_programs::timed_out;

/// A bound from the protocol arrives as a bound, with the program and deadline.
#[test]
fn a_bound_from_the_protocol_reaches_the_php_area_as_a_bound() {
    // The three it could be mistaken for each tell the operator the program ANSWERED:
    // a pool refused, a reload refused, a write that failed. A killed program answered
    // nothing.
    let error = PhpOpError::from(SafeWriteError::Bound(timed_out("/usr/sbin/php-fpm8.3")));

    match error {
        PhpOpError::Bound(bound) => assert_eq!(bound, timed_out("/usr/sbin/php-fpm8.3")),
        other => panic!("expected Bound, got {other:?}"),
    }
}

/// The inverse controls: the other failures keep the variants they had.
#[test]
fn the_other_protocol_failures_keep_their_php_variants() {
    assert!(matches!(
        PhpOpError::from(SafeWriteError::ValidationFailed {
            stderr: "bad".to_owned()
        }),
        PhpOpError::PoolValidation { .. }
    ));
    assert!(matches!(
        PhpOpError::from(SafeWriteError::ReloadFailed {
            stderr: "bad".to_owned()
        }),
        PhpOpError::ReloadFailed { .. }
    ));
    assert!(matches!(
        PhpOpError::from(SafeWriteError::SpawnFailed {
            program: "/usr/sbin/php-fpm8.3".to_owned(),
            reason: "missing".to_owned()
        }),
        PhpOpError::ConfigWrite { .. }
    ));
}
