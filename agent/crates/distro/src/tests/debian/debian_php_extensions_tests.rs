//! Tests for the Debian family's PHP extension packages, switches and ini directory.
//!
//! The mapping is the author's reading of the repository and no fixtures were
//! recorded (no container or network when written), so these tests pin the
//! SHAPE of what is answered: package names built from the validated version
//! and catalogue name, absolute programs, and an argv holding nothing else.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;

use super::{
    php_extension_package, php_extension_switch, php_ini_drop_in_directory, phpdismod_binary,
    phpenmod_binary,
};
use crate::PhpExtensionFacts;
use crate::debian::DebianAdapter;

/// Parses a catalogue name the test knows is valid.
fn extension(name: &str) -> PhpExtensionName {
    PhpExtensionName::parse(name).unwrap()
}

/// Parses a version the test knows is valid.
fn version(text: &str) -> PhpVersion {
    PhpVersion::parse(text).unwrap()
}

#[test]
fn an_extension_package_is_named_after_the_version_with_its_dot_and_the_extension() {
    assert_eq!(
        php_extension_package(&version("8.3"), extension("gd")),
        Some("php8.3-gd".to_owned())
    );
}

#[test]
fn the_mysql_xml_opcache_and_imagick_extensions_keep_their_catalogue_names_in_the_package() {
    let eight_three = version("8.3");

    for (name, package) in [
        ("mysql", "php8.3-mysql"),
        ("xml", "php8.3-xml"),
        ("opcache", "php8.3-opcache"),
        ("imagick", "php8.3-imagick"),
    ] {
        assert_eq!(
            php_extension_package(&eight_three, extension(name)),
            Some(package.to_owned()),
            "{name}"
        );
    }
}

#[test]
fn every_catalogue_extension_has_a_package_on_the_debian_family() {
    for candidate in PhpExtensionName::catalogue() {
        assert!(
            php_extension_package(&version("8.2"), candidate).is_some(),
            "{} has no package",
            candidate.as_str()
        );
    }
}

#[test]
fn the_switch_runs_phpenmod_to_enable_and_phpdismod_to_disable() {
    let switch = php_extension_switch(&version("8.3"), extension("gd")).unwrap();

    assert_eq!(switch.enable_program, "/usr/sbin/phpenmod");
    assert_eq!(switch.disable_program, "/usr/sbin/phpdismod");
}

#[test]
fn the_switch_arguments_name_the_version_the_fpm_sapi_and_the_extension() {
    let switch = php_extension_switch(&version("8.3"), extension("gd")).unwrap();
    let expected = ["-v", "8.3", "-s", "fpm", "gd"];

    assert_eq!(switch.enable_arguments, expected);
    assert_eq!(switch.disable_arguments, expected);
}

#[test]
fn the_mysql_extension_is_switched_through_its_mysqli_ini_module() {
    let switch = php_extension_switch(&version("8.1"), extension("mysql")).unwrap();

    assert_eq!(switch.enable_arguments.last().unwrap(), "mysqli");
    assert_eq!(switch.disable_arguments.last().unwrap(), "mysqli");
}

#[test]
fn every_catalogue_extension_has_a_switch_on_the_debian_family() {
    for candidate in PhpExtensionName::catalogue() {
        assert!(
            php_extension_switch(&version("8.4"), candidate).is_some(),
            "{} has no switch",
            candidate.as_str()
        );
    }
}

#[test]
fn the_switch_programs_are_absolute_paths() {
    assert!(phpenmod_binary().as_path().is_absolute());
    assert!(phpdismod_binary().as_path().is_absolute());
}

#[test]
fn the_ini_drop_in_directory_is_the_versions_fpm_conf_d() {
    assert_eq!(
        php_ini_drop_in_directory(&version("8.3")),
        "/etc/php/8.3/fpm/conf.d"
    );
}

#[test]
fn the_debian_adapter_answers_the_php_extension_facts_from_this_family() {
    let adapter = DebianAdapter;
    let seven_four = version("7.4");

    assert_eq!(
        adapter.php_extension_package(&seven_four, extension("zip")),
        Some("php7.4-zip".to_owned())
    );
    assert!(
        adapter
            .php_extension_switch(&seven_four, extension("zip"))
            .is_some()
    );
    assert_eq!(
        adapter.php_ini_drop_in_directory(&seven_four),
        "/etc/php/7.4/fpm/conf.d"
    );
}
