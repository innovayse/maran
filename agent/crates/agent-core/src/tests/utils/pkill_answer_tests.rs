//! Tests for [`PkillAnswer`], the classification of `pkill`'s exit status.

use crate::utils::pkill_answer::PkillAnswer;

#[test]
fn status_zero_means_a_process_matched() {
    assert_eq!(PkillAnswer::from_status(0), PkillAnswer::Matched);
}

#[test]
fn status_one_means_nothing_matched() {
    assert_eq!(PkillAnswer::from_status(1), PkillAnswer::NothingMatched);
}

#[test]
fn every_other_status_is_a_refusal_carrying_it() {
    for status in [2, 3, 127, -1, 137] {
        assert_eq!(
            PkillAnswer::from_status(status),
            PkillAnswer::Refused(status)
        );
    }
}
