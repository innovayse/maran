//! Tests for the revalidation of an app action.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::apps::AppServiceAction;

use super::validated_action;
use crate::proto::{AppServiceAction as WireAction, ErrorCode};

#[test]
fn each_known_action_maps_to_its_own_operation() {
    assert_eq!(
        validated_action(WireAction::Start as i32).unwrap(),
        AppServiceAction::Start
    );
    assert_eq!(
        validated_action(WireAction::Stop as i32).unwrap(),
        AppServiceAction::Stop
    );
    assert_eq!(
        validated_action(WireAction::Restart as i32).unwrap(),
        AppServiceAction::Restart
    );
    assert_eq!(
        validated_action(WireAction::Reload as i32).unwrap(),
        AppServiceAction::Reload
    );
}

#[test]
fn an_unset_or_unknown_action_is_invalid_input_and_never_a_default() {
    for action in [WireAction::Unspecified as i32, 99] {
        let error = validated_action(action).unwrap_err();
        assert_eq!(error.code, ErrorCode::InvalidInput as i32);
    }
}
