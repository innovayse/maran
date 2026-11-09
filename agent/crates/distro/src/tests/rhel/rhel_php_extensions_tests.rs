//! Tests for the RHEL family's PHP extension packages, switches and ini directory.
//!
//! The mapping is the author's reading of Remi's package list and no fixtures
//! were recorded (no container or network when written), so these tests pin
//! the names this code claims, not a repository's answer.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use super::{php_extension_package, php_extension_switch, php_ini_drop_in_directory};
use crate::PhpExtensionFacts;
use crate::rhel::RhelAdapter;

/// Parses a catalogue name the test knows is valid.
fn extension(name: &str) -> PhpExtensionName {
    PhpExtensionName::parse(name).unwrap()
}

/// Parses a version the test knows is valid.
fn version(text: &str) -> PhpVersion {
    PhpVersion::parse(text).unwrap()
}

/// The package this family names for `name` on `text`.
fn package(text: &str, name: &str) -> Option<String> {
    php_extension_package(&version(text), extension(name))
}

#[test]
fn an_ordinary_extension_package_drops_the_dot_and_repeats_php() {
    assert_eq!(package("8.3", "gd"), Some("php83-php-gd".to_owned()));
}

#[test]
fn the_mysql_extension_is_the_mysqlnd_package() {
    assert_eq!(
        package("8.2", "mysql"),
        Some("php82-php-mysqlnd".to_owned())
    );
}

#[test]
fn the_zip_extension_is_a_pecl_package() {
    assert_eq!(package("8.3", "zip"), Some("php83-php-pecl-zip".to_owned()));
}

#[test]
fn redis_is_the_six_series_pecl_package() {
    assert_eq!(
        package("8.3", "redis"),
        Some("php83-php-pecl-redis6".to_owned())
    );
}

#[test]
fn redis_on_seven_four_is_the_five_series_pecl_package() {
    assert_eq!(
        package("7.4", "redis"),
        Some("php74-php-pecl-redis5".to_owned())
    );
}

#[test]
fn the_imagick_extension_is_the_imagemagick_seven_pecl_package() {
    assert_eq!(
        package("8.4", "imagick"),
        Some("php84-php-pecl-imagick-im7".to_owned())
    );
}

#[test]
fn curl_has_no_package_of_its_own_because_it_is_built_in() {
    for text in ["7.4", "8.0", "8.4"] {
        assert_eq!(package(text, "curl"), None, "{text}");
    }
}

#[test]
fn every_other_catalogue_extension_has_a_package() {
    for candidate in PhpExtensionName::catalogue().filter(|c| c.as_str() != "curl") {
        assert!(
            php_extension_package(&version("8.3"), candidate).is_some(),
            "{} has no package",
            candidate.as_str()
        );
    }
}

#[test]
fn no_extension_can_be_switched_off_on_the_rhel_family() {
    for candidate in PhpExtensionName::catalogue() {
        assert!(
            php_extension_switch(&version("8.3"), candidate).is_none(),
            "{} has a switch",
            candidate.as_str()
        );
    }
}

#[test]
fn the_ini_drop_in_directory_is_the_versions_php_d_without_the_dot() {
    assert_eq!(
        php_ini_drop_in_directory(&version("8.3")),
        "/etc/opt/remi/php83/php.d"
    );
}

#[test]
fn the_rhel_adapter_answers_the_php_extension_facts_from_this_family() {
    let adapter = RhelAdapter;
    let eight_one = version("8.1");

    assert_eq!(
        adapter.php_extension_package(&eight_one, extension("mysql")),
        Some("php81-php-mysqlnd".to_owned())
    );
    assert!(
        adapter
            .php_extension_switch(&eight_one, extension("mysql"))
            .is_none()
    );
    assert_eq!(
        adapter.php_ini_drop_in_directory(&eight_one),
        "/etc/opt/remi/php81/php.d"
    );
}
