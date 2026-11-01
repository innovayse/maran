//! Tests for [`account_slice_unit`].

use crate::utils::account_slice_unit::account_slice_unit;

/// The slice is named from the numeric uid, as systemd-logind names it.
#[test]
fn the_slice_is_named_from_the_numeric_uid() {
    assert_eq!(account_slice_unit(1042), "user-1042.slice");
}
