//! Tests for the `alias_fanout` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{AliasFanout, FanoutRecipient, MAXIMUM_FANOUT};
use crate::validation::mail::alias_fanout_error::AliasFanoutError;

#[test]
fn an_alias_with_outside_deliveries_parses_lower_cased() {
    let fanout = AliasFanout::parse("Sales@Example.test", 3).unwrap();

    assert_eq!(fanout.recipient.to_key(), "sales@example.test");
    assert_eq!(fanout.external_targets, 3);
}

#[test]
fn a_catch_all_parses_under_its_at_key() {
    let fanout = AliasFanout::parse("@Example.test", 3).unwrap();

    assert!(matches!(fanout.recipient, FanoutRecipient::CatchAll(_)));
    assert_eq!(fanout.recipient.to_key(), "@example.test");
    assert_eq!(fanout.recipient.domain().as_str(), "example.test");
}

#[test]
fn a_bad_catch_all_domain_is_refused() {
    for bad in ["@", "@localhost", "@a b.test", "@a@b.test"] {
        assert!(AliasFanout::parse(bad, 2).is_err(), "{bad}");
    }
}

fn limit() -> u32 {
    u32::try_from(MAXIMUM_FANOUT).unwrap()
}

#[test]
fn the_count_bounds_are_inclusive() {
    assert!(AliasFanout::parse("a@example.test", 0).is_ok());
    assert!(AliasFanout::parse("a@example.test", 1).is_ok());
    assert!(AliasFanout::parse("a@example.test", limit()).is_ok());
}

/// The bound is the expansion limit of Postfix, not the size of one alias's own list:
/// an alias of aliases makes more deliveries than any one list holds.
#[test]
fn a_fanout_larger_than_one_list_but_within_the_expansion_limit_is_accepted() {
    assert!(AliasFanout::parse("a@example.test", 21).is_ok());
    assert!(AliasFanout::parse("a@example.test", 100).is_ok());
}

#[test]
fn more_than_postfix_expands_is_refused() {
    for count in [limit() + 1, u32::MAX] {
        assert_eq!(
            AliasFanout::parse("a@example.test", count),
            Err(AliasFanoutError::ExternalTargetsOutOfRange {
                maximum: MAXIMUM_FANOUT
            }),
            "count {count}"
        );
    }
}

/// An alias that stays inside the account is listed with zero outside deliveries.
#[test]
fn an_inside_alias_parses_with_zero_outside_deliveries() {
    let fanout = AliasFanout::parse("team@example.test", 0).unwrap();

    assert_eq!(fanout.external_targets, 0);
    assert_eq!(fanout.recipient.to_key(), "team@example.test");
}

#[test]
fn an_alias_that_could_split_a_map_line_is_refused() {
    assert!(matches!(
        AliasFanout::parse("a@example.test\nb 1", 2),
        Err(AliasFanoutError::Recipient(_))
    ));
}
