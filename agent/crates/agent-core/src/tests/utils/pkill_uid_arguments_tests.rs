//! Tests for [`pkill_uid_arguments`], the one spelling of the uid matcher.

use crate::utils::pkill_uid_arguments::pkill_uid_arguments;

#[test]
fn the_arguments_name_the_signal_ask_for_a_count_and_match_the_real_uid() {
    assert_eq!(
        pkill_uid_arguments("KILL", 1001),
        ["--signal", "KILL", "--count", "--uid", "1001"]
    );
}

#[test]
fn signal_zero_is_passed_through_unchanged_so_a_check_uses_the_same_matcher() {
    assert_eq!(
        pkill_uid_arguments("0", 4242),
        ["--signal", "0", "--count", "--uid", "4242"]
    );
}
