//! Tests for the SUSE family's PHP extension packages, switches and ini directory.
//!
//! The mapping is the author's reading of the distribution's `php8` package naming and no
//! fixtures were recorded (no SUSE container when written), so these tests pin the names this
//! code claims, not a repository's answer. The check against a real openSUSE Leap repository is
//! owed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use super::{php_extension_package, php_extension_switch, php_ini_drop_in_directory};
use crate::PhpExtensionFacts;
use crate::suse::SuseAdapter;

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
fn an_extension_package_of_the_distributions_php_is_the_unversioned_php8_name() {
    assert_eq!(package("8.4", "gd"), Some("php8-gd".to_owned()));
}

#[test]
fn the_catalogue_name_is_the_suffix_with_no_exception() {
    assert_eq!(package("8.4", "mysql"), Some("php8-mysql".to_owned()));
    assert_eq!(package("8.4", "zip"), Some("php8-zip".to_owned()));
    assert_eq!(package("8.4", "redis"), Some("php8-redis".to_owned()));
}

#[test]
fn every_catalogue_extension_has_a_package_of_its_own() {
    for candidate in PhpExtensionName::catalogue() {
        assert_eq!(
            php_extension_package(&version("8.4"), candidate),
            Some(format!("php8-{}", candidate.as_str())),
            "{}",
            candidate.as_str()
        );
    }
}

#[test]
fn an_extension_of_another_version_names_a_package_no_repository_carries() {
    // Inverse control to the tests above: the same extension on a version this family does not
    // offer must not resolve to the package of the one it does.
    for text in ["7.4", "8.0", "8.1", "8.2", "8.3"] {
        let named = package(text, "gd");

        assert_eq!(named, Some(format!("php{text}-gd")));
        assert_ne!(named, package("8.4", "gd"), "{text}");
    }
}

#[test]
fn no_extension_can_be_switched_off_on_the_suse_family() {
    for candidate in PhpExtensionName::catalogue() {
        assert!(
            php_extension_switch(&version("8.4"), candidate).is_none(),
            "{} has a switch",
            candidate.as_str()
        );
    }
}

#[test]
fn the_ini_drop_in_directory_is_the_one_php8_conf_d() {
    assert_eq!(
        php_ini_drop_in_directory(&version("8.4")),
        "/etc/php8/conf.d"
    );
}

#[test]
fn the_ini_drop_in_directory_does_not_depend_on_the_version() {
    assert_eq!(
        php_ini_drop_in_directory(&version("8.3")),
        php_ini_drop_in_directory(&version("8.4"))
    );
}

#[test]
fn the_suse_adapter_answers_the_php_extension_facts_from_this_family() {
    let adapter = SuseAdapter;
    let eight_four = version("8.4");

    assert_eq!(
        adapter.php_extension_package(&eight_four, extension("mysql")),
        Some("php8-mysql".to_owned())
    );
    assert!(
        adapter
            .php_extension_switch(&eight_four, extension("mysql"))
            .is_none()
    );
    assert_eq!(
        adapter.php_ini_drop_in_directory(&eight_four),
        "/etc/php8/conf.d"
    );
}
