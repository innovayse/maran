//! Tests for [`ValidatedAppId`]: a closed catalogue, so what matters is what it refuses.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::ValidatedAppId;
use crate::validation::apps::app_id_error::AppIdError;

#[test]
fn every_catalogue_spelling_parses_and_reads_back_unchanged() {
    for id in [
        "nginx",
        "apache",
        "php-7.4",
        "php-8.3",
        "php-8.4",
        "nodejs-18",
        "nodejs-20",
        "nodejs-22",
        "nodejs-24",
    ] {
        assert_eq!(ValidatedAppId::parse(id).unwrap().as_id(), id);
    }
}

#[test]
fn only_the_two_web_servers_are_web_servers() {
    assert!(ValidatedAppId::parse("nginx").unwrap().is_web_server());
    assert!(ValidatedAppId::parse("apache").unwrap().is_web_server());
    assert!(!ValidatedAppId::parse("php-8.3").unwrap().is_web_server());
    assert!(!ValidatedAppId::parse("nodejs-20").unwrap().is_web_server());
}

#[test]
fn a_package_name_is_not_an_app_id() {
    for candidate in [
        "apache2",
        "httpd",
        "php8.3-fpm",
        "nodejs",
        "php",
        "php-",
        "nodejs-",
    ] {
        assert!(
            matches!(
                ValidatedAppId::parse(candidate),
                Err(AppIdError::Unknown { .. })
            ),
            "{candidate}"
        );
    }
}

#[test]
fn a_node_major_outside_the_catalogue_is_unknown() {
    assert!(matches!(
        ValidatedAppId::parse("nodejs-19"),
        Err(AppIdError::Unknown { .. })
    ));
    assert!(matches!(
        ValidatedAppId::parse("nodejs-20.1"),
        Err(AppIdError::Unknown { .. })
    ));
}

#[test]
fn a_php_version_that_is_not_two_components_is_unknown() {
    for candidate in ["php-8", "php-8.3.1", "php-x.y", "php-8.3-rc"] {
        assert!(
            matches!(
                ValidatedAppId::parse(candidate),
                Err(AppIdError::Unknown { .. })
            ),
            "{candidate}"
        );
    }
}

#[test]
fn an_option_or_a_path_is_unknown() {
    for candidate in [
        "-oDpkg::Pre-Invoke=sh",
        "../nginx",
        "/usr/bin/nginx",
        "Nginx",
    ] {
        assert!(ValidatedAppId::parse(candidate).is_err(), "{candidate}");
    }
}

#[test]
fn a_control_character_is_refused_by_name() {
    assert_eq!(
        ValidatedAppId::parse("nginx\nExecStart=/bin/sh"),
        Err(AppIdError::ControlCharacter)
    );
    assert_eq!(
        ValidatedAppId::parse("php-8.3\0"),
        Err(AppIdError::ControlCharacter)
    );
}

#[test]
fn an_empty_id_is_refused() {
    assert_eq!(ValidatedAppId::parse(""), Err(AppIdError::Empty));
}

#[test]
fn an_overlong_id_is_refused() {
    assert!(matches!(
        ValidatedAppId::parse(&"a".repeat(64)),
        Err(AppIdError::TooLong { .. })
    ));
}
