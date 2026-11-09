//! What the pool-settings revalidation accepts and refuses.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::validated_pool_settings;
use crate::proto::{ApplyPhpPoolSettingsRequest, ErrorCode, PhpSetting};

/// A request for `acme` at `version` with the given `(name, value)` settings.
fn request(
    version: &str,
    max_children: u32,
    settings: &[(&str, &str)],
) -> ApplyPhpPoolSettingsRequest {
    ApplyPhpPoolSettingsRequest {
        account_username: "acme".to_owned(),
        version: version.to_owned(),
        max_children,
        overrides: settings
            .iter()
            .map(|(name, value)| PhpSetting {
                name: (*name).to_owned(),
                value: (*value).to_owned(),
            })
            .collect(),
    }
}

/// The validated account the handler hands over.
fn account() -> AccountName {
    AccountName::parse("acme").unwrap()
}

#[test]
fn a_well_formed_request_becomes_the_input_the_pool_writer_takes() {
    let input = validated_pool_settings(
        account(),
        &request(
            "8.3",
            12,
            &[("memory_limit", "256M"), ("max_input_vars", "3000")],
        ),
    )
    .unwrap();

    assert_eq!(input.account.as_str(), "acme");
    assert_eq!(input.version.as_str(), "8.3");
    assert_eq!(input.max_children, 12);
    let names: Vec<&str> = input.overrides.iter().map(|item| item.name()).collect();
    assert_eq!(names, vec!["memory_limit", "max_input_vars"]);
}

#[test]
fn a_request_with_no_settings_is_accepted_and_carries_none() {
    let input = validated_pool_settings(account(), &request("8.3", 10, &[])).unwrap();

    assert!(input.overrides.is_empty());
}

#[test]
fn a_malformed_version_is_refused_as_invalid_input() {
    for version in ["", "8", "8.3.1", "8.3\n", "../8.3", "eight.three"] {
        let refused = validated_pool_settings(account(), &request(version, 10, &[])).unwrap_err();

        assert_eq!(refused.code, ErrorCode::InvalidInput as i32, "{version:?}");
    }
}

#[test]
fn a_setting_name_off_the_whitelist_is_refused_and_not_dropped() {
    let refused = validated_pool_settings(
        account(),
        &request(
            "8.3",
            10,
            &[("memory_limit", "256M"), ("open_basedir", "/")],
        ),
    )
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
    assert!(
        refused.message.contains("open_basedir"),
        "{}",
        refused.message
    );
}

#[test]
fn a_value_carrying_a_newline_is_refused() {
    let refused = validated_pool_settings(
        account(),
        &request("8.3", 10, &[("memory_limit", "256M\nphp_admin_value[x]=1")]),
    )
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn a_post_size_below_the_upload_size_is_refused() {
    let refused = validated_pool_settings(
        account(),
        &request(
            "8.3",
            10,
            &[("upload_max_filesize", "64M"), ("post_max_size", "8M")],
        ),
    )
    .unwrap_err();

    assert_eq!(refused.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn the_worker_budget_is_carried_as_sent_for_the_operation_to_judge() {
    // The range is the operation's decision (it refuses, never clamps), so a
    // value outside it must reach the operation unchanged and not be rewritten
    // here into one that passes.
    let input = validated_pool_settings(account(), &request("8.3", 100_000, &[])).unwrap();

    assert_eq!(input.max_children, 100_000);
}
