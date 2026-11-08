//! Tests for [`apply_php_pool_settings`].
//!
//! What these pin is the decision the operation makes about a pool that is
//! already on disk: it never creates one, it writes only when the render
//! differs from the file, it refuses what the whitelist refuses instead of
//! dropping it, and it leaves the account's lock free when it returns.
//!
//! Every test uses an account name of its own. The account lock is a
//! process-wide registry and this operation takes it, so two tests sharing a
//! name would refuse each other with `AccountBusy` depending on how the
//! harness schedules its threads.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::accounts::take_account_lock;
use crate::php::apply_php_pool_settings::apply_php_pool_settings;
use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::model::pool_input::PoolInput;
use crate::php::model::pool_paths::PoolPaths;
use crate::php::render_pool::render_pool;
use crate::php::{PhpOpError, PhpOverride};

/// A pool for `account` at `version` with a ten-worker budget and the given
/// `(name, value)` settings, each parsed by the whitelist as the service does.
fn pool(account: &str, version: &str, settings: &[(&str, &str)]) -> PoolInput {
    PoolInput {
        account: AccountName::parse(account).unwrap(),
        version: PhpVersion::parse(version).unwrap(),
        max_children: 10,
        overrides: settings
            .iter()
            .map(|(name, value)| PhpOverride::parse(name, value).unwrap())
            .collect(),
    }
}

/// Where the fake's Debian adapter keeps `input`'s pool file.
fn pool_path(input: &PoolInput) -> std::path::PathBuf {
    PoolPaths::for_pool(distro(), &input.account, &input.version).config_path
}

/// Puts the pool `input` renders to on the host's disk, as an earlier site
/// creation would have, without counting it as a write.
fn place_pool(host: &FakePhpHost, input: &PoolInput) {
    let paths = PoolPaths::for_pool(distro(), &input.account, &input.version);
    host.place_config(
        &paths.config_path,
        &render_pool(distro(), input, &paths).unwrap(),
    );
}

#[test]
fn an_account_with_no_pool_file_is_pool_missing_and_nothing_is_written() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let wanted = pool("applyabsent", "8.3", &[("memory_limit", "256M")]);

    let refusal = apply_php_pool_settings(&host, distro(), &wanted);

    match refusal {
        Err(PhpOpError::PoolMissing { username, version }) => {
            assert_eq!(username, "applyabsent");
            assert_eq!(version, "8.3");
        }
        other => panic!("expected PoolMissing, got {other:?}"),
    }
    // It can never create a pool: no file, no socket directory, no session
    // tree, no validator and no reload.
    assert_eq!(host.writes(), 0);
    assert_eq!(host.config(&pool_path(&wanted)), None);
    assert!(host.created_directories().is_empty());
    assert!(host.created_as_account().is_empty());
    assert!(host.protocol_commands().is_empty());
}

#[test]
fn a_pool_that_already_equals_its_render_is_not_written_and_nothing_is_reloaded() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let wanted = pool("applyequal", "8.3", &[("memory_limit", "256M")]);
    place_pool(&host, &wanted);

    apply_php_pool_settings(&host, distro(), &wanted).unwrap();
    apply_php_pool_settings(&host, distro(), &wanted).unwrap();

    assert_eq!(host.writes(), 0);
    assert!(host.protocol_commands().is_empty());
    assert!(host.commands().is_empty());
}

#[test]
fn changed_settings_are_written_once_validated_and_reloaded() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    place_pool(&host, &pool("applychanged", "8.3", &[]));
    let wanted = pool("applychanged", "8.3", &[("memory_limit", "256M")]);

    apply_php_pool_settings(&host, distro(), &wanted).unwrap();

    assert_eq!(host.writes(), 1);
    let pool_file = host.config(&pool_path(&wanted)).unwrap();
    assert!(
        pool_file.contains("php_value[memory_limit] = 256M"),
        "{pool_file}"
    );
    // The protocol's two commands, validator then reload: the same path every
    // other pool write takes.
    let commands = host.protocol_commands();
    assert_eq!(commands.len(), 2, "{commands:?}");
    assert!(commands[0].contains(&"-t".to_owned()), "{commands:?}");
    assert!(commands[1].contains(&"reload".to_owned()), "{commands:?}");

    // The control: the same request again is now equal to the file, so the
    // count above was the first call's and a retry costs nothing.
    apply_php_pool_settings(&host, distro(), &wanted).unwrap();
    assert_eq!(host.writes(), 1);
}

#[test]
fn a_setting_the_request_no_longer_lists_returns_to_the_pools_default() {
    // The request replaces the set; it does not merge into the file.
    let host = FakePhpHost::with_installed(&["8.3"]);
    place_pool(
        &host,
        &pool("applyreplace", "8.3", &[("memory_limit", "256M")]),
    );
    let wanted = pool("applyreplace", "8.3", &[]);

    apply_php_pool_settings(&host, distro(), &wanted).unwrap();

    let pool_file = host.config(&pool_path(&wanted)).unwrap();
    assert!(!pool_file.contains("memory_limit"), "{pool_file}");
    assert_eq!(host.writes(), 1);
}

#[test]
fn a_setting_name_outside_the_whitelist_is_refused_by_the_parser_and_never_dropped() {
    // The operation takes no strings: the only way to a `PhpOverride` is the
    // whitelist's parser, so a name it does not list cannot reach the render.
    for name in ["disable_functions", "open_basedir", "php_admin_value[x]"] {
        match PhpOverride::parse(name, "1") {
            Err(PhpOpError::OverrideNotAllowed { name: refused }) => assert_eq!(refused, name),
            other => panic!("expected OverrideNotAllowed for {name}, got {other:?}"),
        }
    }
}

#[test]
fn a_post_max_size_below_the_upload_limit_is_refused_and_the_pool_is_left_alone() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let current = pool("applyconsistent", "8.3", &[]);
    place_pool(&host, &current);
    let before = host.config(&pool_path(&current)).unwrap();
    let wanted = pool(
        "applyconsistent",
        "8.3",
        &[("upload_max_filesize", "64M"), ("post_max_size", "8M")],
    );

    let refusal = apply_php_pool_settings(&host, distro(), &wanted);

    assert!(
        matches!(refusal, Err(PhpOpError::OverridePostSizeBelowUpload { .. })),
        "{refusal:?}"
    );
    assert_eq!(host.writes(), 0);
    assert_eq!(host.config(&pool_path(&current)).unwrap(), before);
}

#[test]
fn a_version_that_is_not_installed_is_refused_before_the_pool_is_read() {
    let host = FakePhpHost::with_installed(&["8.2"]);
    let wanted = pool("applynoversion", "8.3", &[]);

    let refusal = apply_php_pool_settings(&host, distro(), &wanted);

    match refusal {
        Err(PhpOpError::PhpVersionNotInstalled { version }) => assert_eq!(version, "8.3"),
        other => panic!("expected PhpVersionNotInstalled, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_version_outside_the_supported_set_is_refused() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let wanted = pool("applyunsupported", "9.9", &[]);

    let refusal = apply_php_pool_settings(&host, distro(), &wanted);

    assert!(
        matches!(refusal, Err(PhpOpError::UnsupportedVersion { .. })),
        "{refusal:?}"
    );
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_worker_budget_outside_the_agents_range_is_refused_not_clamped() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let mut wanted = pool("applybudget", "8.3", &[]);
    place_pool(&host, &wanted);

    for requested in [0, 257, u32::MAX] {
        wanted.max_children = requested;

        let refusal = apply_php_pool_settings(&host, distro(), &wanted);

        assert!(
            matches!(
                refusal,
                Err(PhpOpError::WorkerBudgetOutOfRange { requested: seen, .. }) if seen == requested
            ),
            "{requested}: {refusal:?}"
        );
    }
    assert_eq!(host.writes(), 0);
}

#[test]
fn a_pool_php_fpm_refuses_is_reported_and_the_previous_pool_stays() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let current = pool("applyrefused", "8.3", &[]);
    place_pool(&host, &current);
    let before = host.config(&pool_path(&current)).unwrap();
    host.reject_validation("ERROR: failed to post process the configuration");

    let refusal = apply_php_pool_settings(
        &host,
        distro(),
        &pool("applyrefused", "8.3", &[("memory_limit", "256M")]),
    );

    assert!(
        matches!(refusal, Err(PhpOpError::PoolValidation { .. })),
        "{refusal:?}"
    );
    assert_eq!(host.config(&pool_path(&current)).unwrap(), before);
}

#[test]
fn an_account_another_operation_holds_is_busy_and_nothing_is_read_or_written() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let current = pool("applybusy", "8.3", &[]);
    place_pool(&host, &current);
    let wanted = pool("applybusy", "8.3", &[("memory_limit", "256M")]);
    let held = take_account_lock(&wanted.account).expect("the lock is free at the start");

    let refusal = apply_php_pool_settings(&host, distro(), &wanted);

    match refusal {
        Err(PhpOpError::AccountBusy { username }) => assert_eq!(username, "applybusy"),
        other => panic!("expected AccountBusy, got {other:?}"),
    }
    assert_eq!(host.writes(), 0);

    // The control: with the lock free the same request is applied, so the
    // refusal above was the lock and nothing else.
    drop(held);
    apply_php_pool_settings(&host, distro(), &wanted).unwrap();
    assert_eq!(host.writes(), 1);
}

#[test]
fn the_accounts_lock_is_free_again_after_every_outcome() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    let wanted = pool("applyrelease", "8.3", &[]);

    // An error return, then a success: both must release it.
    assert!(apply_php_pool_settings(&host, distro(), &wanted).is_err());
    assert!(take_account_lock(&wanted.account).is_some());

    place_pool(&host, &wanted);
    apply_php_pool_settings(&host, distro(), &wanted).unwrap();
    assert!(take_account_lock(&wanted.account).is_some());
}
