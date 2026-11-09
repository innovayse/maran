//! Tests for the `spam_thresholds` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{MAXIMUM_SCORE, SpamThresholds};
use crate::validation::mail::spam_thresholds_error::SpamThresholdsError;

#[test]
fn an_ascending_triple_is_accepted() {
    let thresholds = SpamThresholds::parse(4, 6, 15).unwrap();

    assert_eq!(
        (
            thresholds.greylist(),
            thresholds.add_header(),
            thresholds.reject()
        ),
        (4, 6, 15)
    );
}

#[test]
fn a_zero_score_is_refused_rather_than_read_as_never() {
    assert_eq!(
        SpamThresholds::parse(0, 6, 15),
        Err(SpamThresholdsError::Zero)
    );
    assert_eq!(
        SpamThresholds::parse(4, 0, 15),
        Err(SpamThresholdsError::Zero)
    );
    assert_eq!(
        SpamThresholds::parse(4, 6, 0),
        Err(SpamThresholdsError::Zero)
    );
}

#[test]
fn a_score_above_the_ceiling_is_refused() {
    assert!(SpamThresholds::parse(4, 6, MAXIMUM_SCORE).is_ok());
    assert_eq!(
        SpamThresholds::parse(4, 6, MAXIMUM_SCORE + 1),
        Err(SpamThresholdsError::TooLarge {
            maximum: MAXIMUM_SCORE
        })
    );
}

#[test]
fn equal_or_descending_scores_are_refused() {
    for (a, b, c) in [(6, 6, 15), (4, 15, 15), (15, 6, 4), (4, 15, 6)] {
        assert_eq!(
            SpamThresholds::parse(a, b, c),
            Err(SpamThresholdsError::NotAscending),
            "{a} {b} {c}"
        );
    }
}
