//! Tests for [`php_version_in`]: finding a PHP version in a path or a handler line.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::php_version_in::php_version_in;

/// A bare version is the version.
#[test]
fn a_bare_version_is_read() {
    assert_eq!(php_version_in("8.2").as_deref(), Some("8.2"));
    assert_eq!(php_version_in("  7.4\n").as_deref(), Some("7.4"));
}

/// The version in a socket path is read after the word php.
#[test]
fn a_socket_path_names_its_version() {
    assert_eq!(
        php_version_in("unix:/run/php/php8.2-fpm.sock").as_deref(),
        Some("8.2")
    );
    assert_eq!(
        php_version_in("/var/run/php-fpm8.1.sock").as_deref(),
        Some("8.1")
    );
}

/// A handler line names the version too.
#[test]
fn a_handler_line_names_its_version() {
    assert_eq!(
        php_version_in("SetHandler \"proxy:unix:/run/php/php8.3-fpm.sock|fcgi://localhost\"")
            .as_deref(),
        Some("8.3")
    );
}

/// The compact form directly after php is split into major and minor.
#[test]
fn the_compact_form_is_split() {
    assert_eq!(php_version_in("php82").as_deref(), Some("8.2"));
    assert_eq!(
        php_version_in("unix:/var/opt/remi/php74/run/php-fpm/www.sock").as_deref(),
        Some("7.4")
    );
}

/// Inverse controls: an address, a three-part version and text without the word
/// php are not versions.
#[test]
fn addresses_and_longer_numbers_are_not_versions() {
    assert_eq!(php_version_in("127.0.0.1:9000"), None);
    assert_eq!(php_version_in("php-fpm 10.0.0.5:9000"), None);
    assert_eq!(php_version_in("php8.2.1"), None);
    assert_eq!(php_version_in("unix:/run/app/app3.4.sock"), None);
    assert_eq!(php_version_in(""), None);
}

/// A name the word php is merely part of, with no number after it, is nothing.
#[test]
fn php_without_a_number_is_nothing() {
    assert_eq!(php_version_in("/run/php/php-fpm.sock"), None);
    assert_eq!(php_version_in("composer"), None);
}

/// Hostile input is read without panicking.
#[test]
fn hostile_input_does_not_panic() {
    let long = format!("php{}", "9".repeat(10_000));
    assert_eq!(php_version_in(&long), None);
    assert_eq!(
        php_version_in("php\u{fffd}\u{fffd}8.2").as_deref(),
        Some("8.2")
    );
    assert_eq!(php_version_in("php."), None);
}
