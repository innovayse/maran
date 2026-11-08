//! Tests for [`OpcacheDropIn`]: the render equals its golden byte for byte, and
//! each isolation line is present whatever the sizes are.
//!
//! Tests mirror the source tree under `src/tests/` (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::OpcacheDropIn;

/// The golden render, read at compile time from the crate's golden folder.
const GOLDEN: &str = include_str!("../../../tests/golden/php_fpm/opcache.ini");

/// The sizes the golden was written with.
fn default_drop_in() -> OpcacheDropIn {
    OpcacheDropIn {
        memory_consumption_mib: 128,
        interned_strings_buffer_mib: 16,
        max_accelerated_files: 10000,
    }
}

#[test]
fn the_drop_in_renders_its_golden_byte_for_byte() {
    assert_eq!(default_drop_in().render_config().unwrap(), GOLDEN);
}

#[test]
fn the_cache_validates_permission_so_a_pool_never_receives_another_accounts_script() {
    let rendered = default_drop_in().render_config().unwrap();

    assert!(
        rendered
            .lines()
            .any(|line| line == "opcache.validate_permission=1"),
        "{rendered}"
    );
}

#[test]
fn the_cache_validates_root() {
    let rendered = default_drop_in().render_config().unwrap();

    assert!(
        rendered
            .lines()
            .any(|line| line == "opcache.validate_root=1"),
        "{rendered}"
    );
}

#[test]
fn the_cache_api_is_restricted_to_a_prefix_and_never_left_empty() {
    // An empty `restrict_api` means unrestricted, which would leave every
    // tenant able to reset the shared cache and list its contents.
    let rendered = default_drop_in().render_config().unwrap();

    let value = rendered
        .lines()
        .find_map(|line| line.strip_prefix("opcache.restrict_api="))
        .expect("the drop-in has no restrict_api line");
    assert!(value.starts_with('/'), "{value}");
    assert!(value.len() > 1, "{value}");
}

#[test]
fn the_three_sizes_reach_their_own_lines() {
    let rendered = OpcacheDropIn {
        memory_consumption_mib: 256,
        interned_strings_buffer_mib: 32,
        max_accelerated_files: 20000,
    }
    .render_config()
    .unwrap();

    for expected in [
        "opcache.memory_consumption=256",
        "opcache.interned_strings_buffer=32",
        "opcache.max_accelerated_files=20000",
    ] {
        assert!(rendered.lines().any(|line| line == expected), "{rendered}");
    }
}

#[test]
fn the_isolation_lines_survive_a_change_of_sizes() {
    let rendered = OpcacheDropIn {
        memory_consumption_mib: 64,
        interned_strings_buffer_mib: 8,
        max_accelerated_files: 4000,
    }
    .render_config()
    .unwrap();

    for expected in [
        "opcache.validate_permission=1",
        "opcache.validate_root=1",
        "opcache.restrict_api=/nonexistent-maran-opcache-api",
    ] {
        assert!(rendered.lines().any(|line| line == expected), "{rendered}");
    }
}
