//! Tests for the `record_priority` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::record_priority::RecordPriority;
use super::super::record_priority_error::RecordPriorityError;

#[test]
fn zero_and_65535_are_accepted_and_65536_is_refused() {
    assert_eq!(RecordPriority::parse(0).unwrap().value(), 0);
    assert_eq!(RecordPriority::parse(65_535).unwrap().value(), 65_535);
    assert_eq!(
        RecordPriority::parse(65_536),
        Err(RecordPriorityError::TooLarge { value: 65_536 })
    );
}
