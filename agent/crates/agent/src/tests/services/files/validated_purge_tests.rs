//! Tests for `validated_purge`: a zero retention is refused, not read as
//! "everything".

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::validated_purge;
use crate::proto::{ErrorCode, PurgeExpiredTrashRequest};

#[test]
fn a_valid_purge_carries_its_retention() {
    let input = validated_purge(&PurgeExpiredTrashRequest {
        account_username: "alice_01".to_owned(),
        older_than_days: 30,
    })
    .unwrap();

    assert_eq!(input.account.as_str(), "alice_01");
    assert_eq!(input.older_than_days.get(), 30);
}

#[test]
fn a_zero_retention_is_invalid_input_not_everything() {
    let refused = validated_purge(&PurgeExpiredTrashRequest {
        account_username: "alice_01".to_owned(),
        older_than_days: 0,
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_bad_account_name_is_invalid_input() {
    let refused = validated_purge(&PurgeExpiredTrashRequest {
        account_username: "../root".to_owned(),
        older_than_days: 30,
    })
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
