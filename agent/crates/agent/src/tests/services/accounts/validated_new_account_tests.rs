//! Tests for the `validated_new_account` module.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_ops::accounts::{LoginShell, ResourceCeiling};

use super::validated_new_account;
use crate::proto::resource_ceiling::Ceiling;
use crate::proto::{
    CreateAccountRequest, ErrorCode, LoginShell as WireLoginShell, ResourceCeiling as WireCeiling,
};

fn request() -> CreateAccountRequest {
    CreateAccountRequest {
        username: "alice".to_owned(),
        quota_bytes: 4096,
        inode_limit: None,
        login_shell: WireLoginShell::Unspecified as i32,
    }
}

#[test]
fn a_request_written_before_the_new_fields_means_no_inode_ceiling_and_no_login() {
    let settings = validated_new_account(&request()).unwrap();

    assert_eq!(settings.quota_bytes, 4096);
    assert_eq!(settings.inode_limit, ResourceCeiling::Unlimited);
    assert_eq!(settings.shell, LoginShell::Nologin);
}

#[test]
fn an_inode_ceiling_and_a_bash_shell_are_carried_through() {
    let stated = CreateAccountRequest {
        inode_limit: Some(WireCeiling {
            ceiling: Some(Ceiling::Maximum(100_000)),
        }),
        login_shell: WireLoginShell::Bash as i32,
        ..request()
    };

    let settings = validated_new_account(&stated).unwrap();

    assert_eq!(settings.shell, LoginShell::Bash);
    assert!(matches!(
        settings.inode_limit,
        ResourceCeiling::Limited(limit) if limit.value() == 100_000
    ));
}

#[test]
fn a_zero_inode_figure_refuses_the_whole_creation() {
    let stated = CreateAccountRequest {
        inode_limit: Some(WireCeiling {
            ceiling: Some(Ceiling::Maximum(0)),
        }),
        ..request()
    };

    let refused = validated_new_account(&stated).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_shell_the_agent_cannot_name_refuses_the_whole_creation() {
    let stated = CreateAccountRequest {
        login_shell: 77,
        ..request()
    };

    let refused = validated_new_account(&stated).unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}
