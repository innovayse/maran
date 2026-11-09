//! Tests for the `php_extension_name` module.
//!
//! The name reaches an install unit's instance and an argv, both run by root,
//! so what matters is that nothing outside the thirteen can be constructed.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::super::php_extension_name::PhpExtensionName;
use super::super::php_extension_name_error::PhpExtensionNameError;

#[test]
fn a_catalogue_name_parses_and_reads_back_as_written() {
    assert_eq!(PhpExtensionName::parse("gd").unwrap().as_str(), "gd");
}

#[test]
fn every_name_in_the_catalogue_parses_to_itself() {
    for extension in PhpExtensionName::catalogue() {
        assert_eq!(PhpExtensionName::parse(extension.as_str()), Ok(extension));
    }
}

#[test]
fn the_catalogue_holds_thirteen_names_in_alphabetical_order() {
    let names: Vec<&str> = PhpExtensionName::catalogue()
        .map(|extension| extension.as_str())
        .collect();

    assert_eq!(
        names,
        [
            "bcmath", "curl", "gd", "gmp", "imagick", "intl", "mbstring", "mysql", "opcache",
            "redis", "soap", "xml", "zip",
        ]
    );
}

#[test]
fn the_mysql_extension_is_loaded_as_the_mysqli_module() {
    let extension = PhpExtensionName::parse("mysql").unwrap();

    assert_eq!(extension.loaded_module_name(), "mysqli");
}

#[test]
fn the_opcache_extension_is_loaded_as_the_zend_opcache_module() {
    let extension = PhpExtensionName::parse("opcache").unwrap();

    assert_eq!(extension.loaded_module_name(), "Zend OPcache");
}

#[test]
fn an_extension_with_no_special_spelling_is_loaded_under_its_own_name() {
    let extension = PhpExtensionName::parse("mbstring").unwrap();

    assert_eq!(extension.loaded_module_name(), "mbstring");
}

#[test]
fn an_empty_name_is_rejected() {
    assert_eq!(
        PhpExtensionName::parse(""),
        Err(PhpExtensionNameError::Empty)
    );
}

#[test]
fn a_name_containing_a_newline_is_rejected_as_a_control_character() {
    // The injection this type exists to stop. The refusal must be by name and
    // not a side effect of the lookup: `gd\nbash` would also miss the
    // catalogue, and a later loosening of the lookup must not remove it.
    let refused = PhpExtensionName::parse("gd\nbash");

    assert_eq!(refused, Err(PhpExtensionNameError::ControlCharacter));
}

#[test]
fn a_name_containing_a_nul_is_rejected_as_a_control_character() {
    let refused = PhpExtensionName::parse("gd\0");

    assert_eq!(refused, Err(PhpExtensionNameError::ControlCharacter));
}

#[test]
fn a_control_character_wins_over_the_catalogue_lookup() {
    // A name that is both outside the catalogue and holds a control character
    // reports the control character, because that is the check run first.
    let refused = PhpExtensionName::parse("not-an-extension\r");

    assert_eq!(refused, Err(PhpExtensionNameError::ControlCharacter));
}

#[test]
fn a_name_outside_the_catalogue_is_rejected_with_what_was_offered() {
    let refused = PhpExtensionName::parse("xdebug");

    assert_eq!(
        refused,
        Err(PhpExtensionNameError::NotInCatalogue {
            candidate: "xdebug".to_owned()
        })
    );
}

#[test]
fn a_package_name_is_not_an_extension_name() {
    let refused = PhpExtensionName::parse("php8.3-gd");

    assert!(matches!(
        refused,
        Err(PhpExtensionNameError::NotInCatalogue { .. })
    ));
}

#[test]
fn a_path_to_a_shared_object_is_rejected() {
    let refused = PhpExtensionName::parse("/tmp/evil.so");

    assert!(matches!(
        refused,
        Err(PhpExtensionNameError::NotInCatalogue { .. })
    ));
}

#[test]
fn a_catalogue_name_in_another_case_is_rejected() {
    // The module listing is compared case-insensitively; the name a caller
    // supplies is not: exactly the catalogue's spelling or nothing.
    let refused = PhpExtensionName::parse("GD");

    assert!(matches!(
        refused,
        Err(PhpExtensionNameError::NotInCatalogue { .. })
    ));
}

#[test]
fn a_catalogue_name_with_surrounding_whitespace_is_rejected() {
    let refused = PhpExtensionName::parse(" gd");

    assert!(matches!(
        refused,
        Err(PhpExtensionNameError::NotInCatalogue { .. })
    ));
}
