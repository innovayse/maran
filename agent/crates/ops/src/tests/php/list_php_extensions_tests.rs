//! Tests for [`list_php_extensions`].
//!
//! The state of an extension is what `php-fpm<version> -m` prints, so these
//! tests feed it the shape the real binary prints (two section headers, one
//! module per line, `Zend OPcache` with a space in it) and read the answer back.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::bound_exceeded::BoundExceeded;
use maran_agent_core::utils::call_class::CallClass;
use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroFamily, adapter_for};

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::model::extension_availability::ExtensionAvailability;
use crate::php::model::php_extension::PhpExtension;
use crate::php::{PhpOpError, list_php_extensions};

/// What `php-fpm8.3 -m` prints on a host with `mysqli`, `gd` and OPcache.
const MODULE_LISTING: &str =
    "[PHP Modules]\nCore\ngd\nmysqli\nmysqlnd\nPDO\n\n[Zend Modules]\nZend OPcache\n";

/// PHP 8.3, the version every test here asks about.
fn version() -> PhpVersion {
    PhpVersion::parse("8.3").unwrap()
}

/// The catalogue name `text`.
fn name(text: &str) -> PhpExtensionName {
    PhpExtensionName::parse(text).unwrap()
}

/// The entry for `text` in `listing`.
fn entry(listing: &[PhpExtension], text: &str) -> PhpExtension {
    *listing.iter().find(|item| item.name == name(text)).unwrap()
}

/// A Debian host with 8.3 installed whose php-fpm prints `listing`.
fn host_printing(listing: &str) -> FakePhpHost {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.answer("-m", 0, listing, "");
    host
}

#[test]
fn the_listing_has_one_entry_per_catalogue_name_in_alphabetical_order() {
    let host = host_printing(MODULE_LISTING);

    let listing = list_php_extensions(&host, distro(), &version()).unwrap();

    let names: Vec<_> = listing.iter().map(|item| item.name.as_str()).collect();
    let catalogue: Vec<_> = PhpExtensionName::catalogue()
        .map(|item| item.as_str())
        .collect();
    assert_eq!(names, catalogue);
    assert_eq!(names.len(), 13);
    assert!(names.windows(2).all(|pair| pair[0] < pair[1]));
}

#[test]
fn modules_are_matched_by_their_loaded_name_and_not_by_the_catalogue_name() {
    let host = host_printing(MODULE_LISTING);

    let listing = list_php_extensions(&host, distro(), &version()).unwrap();

    // `mysql` is the module `mysqli`, `opcache` is `Zend OPcache`, `gd` is itself.
    assert!(entry(&listing, "mysql").enabled);
    assert!(entry(&listing, "opcache").enabled);
    assert!(entry(&listing, "gd").enabled);
    // Absent from the listing, and `mysqlnd` alone does not stand in for `mysqli`.
    assert!(!entry(&listing, "redis").enabled);
    assert!(!entry(&listing, "imagick").enabled);
}

#[test]
fn a_module_listing_without_mysqli_does_not_report_mysql_even_with_mysqlnd() {
    let host = host_printing("[PHP Modules]\nmysqlnd\n\n[Zend Modules]\n");

    let listing = list_php_extensions(&host, distro(), &version()).unwrap();

    assert!(!entry(&listing, "mysql").enabled);
    assert!(listing.iter().all(|item| !item.enabled));
}

#[test]
fn matching_ignores_case_surrounding_space_and_carriage_returns() {
    let host =
        host_printing("[PHP Modules]\r\n  GD \r\nMySQLi\r\n\r\n[Zend Modules]\r\nzend opcache\r\n");

    let listing = list_php_extensions(&host, distro(), &version()).unwrap();

    assert!(entry(&listing, "gd").enabled);
    assert!(entry(&listing, "mysql").enabled);
    assert!(entry(&listing, "opcache").enabled);
}

#[test]
fn a_section_header_is_never_read_as_a_module() {
    // A module that was literally named like a header would be bracketed too;
    // the point is that the headers themselves add nothing to the loaded set.
    let host = host_printing("[PHP Modules]\n[Zend Modules]\n");

    let listing = list_php_extensions(&host, distro(), &version()).unwrap();

    assert!(listing.iter().all(|item| !item.enabled));
}

#[test]
fn the_probe_is_one_bounded_run_of_the_versions_own_php_fpm() {
    let host = host_printing(MODULE_LISTING);

    list_php_extensions(&host, distro(), &version()).unwrap();

    assert_eq!(
        host.commands(),
        vec![vec![
            distro().php_fpm_binary(&version()).as_str().to_owned(),
            "-m".to_owned()
        ]]
    );
    assert_eq!(host.classes(), vec![CallClass::Validate]);
}

#[test]
fn on_the_debian_family_every_extension_can_be_switched_both_ways() {
    let host = host_printing(MODULE_LISTING);

    let listing =
        list_php_extensions(&host, adapter_for(DistroFamily::Debian), &version()).unwrap();

    assert!(
        listing
            .iter()
            .all(|item| item.availability == ExtensionAvailability::Available)
    );
}

#[test]
fn on_the_rhel_family_curl_is_not_packaged_and_the_rest_cannot_be_switched_off() {
    let rhel = adapter_for(DistroFamily::Rhel);
    let host = FakePhpHost::with_installed_on(rhel, &["8.3"]);
    host.answer("-m", 0, MODULE_LISTING, "");

    let listing = list_php_extensions(&host, rhel, &version()).unwrap();

    assert_eq!(
        entry(&listing, "curl").availability,
        ExtensionAvailability::NotPackaged
    );
    assert_eq!(
        entry(&listing, "gd").availability,
        ExtensionAvailability::DisableNotSupported
    );
}

#[test]
fn on_the_suse_family_every_extension_has_a_package_and_none_can_be_switched_off() {
    // UNVERIFIED: read from the distribution's `php8-<extension>` package naming and from the
    // absence of a `phpdismod`-style tool, not measured; confirm with `rpm -ql php8-gd` and
    // `command -v phpdismod` in the pinned Leap image. Unlike the RHEL family, `curl` has a
    // package of its own, so no entry is `NotPackaged`.
    let suse = adapter_for(DistroFamily::Suse);
    let host = FakePhpHost::with_installed_on(suse, &["8.3"]);
    host.answer("-m", 0, MODULE_LISTING, "");

    let listing = list_php_extensions(&host, suse, &version()).unwrap();

    assert!(
        listing
            .iter()
            .all(|item| item.availability == ExtensionAvailability::DisableNotSupported)
    );
    assert_eq!(
        entry(&listing, "curl").availability,
        ExtensionAvailability::DisableNotSupported
    );
}

#[test]
fn a_probe_that_exits_non_zero_is_a_failed_probe_and_never_an_empty_listing() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.answer("-m", 255, "", "PHP Fatal error: bad ini");

    let error = list_php_extensions(&host, distro(), &version()).unwrap_err();

    assert!(matches!(
        error,
        PhpOpError::ExtensionProbeFailed { ref stderr } if stderr.contains("bad ini")
    ));
}

#[test]
fn a_probe_that_cannot_be_started_is_a_failed_probe() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.fail_to_spawn("-m");

    let error = list_php_extensions(&host, distro(), &version()).unwrap_err();

    assert!(matches!(error, PhpOpError::ExtensionProbeFailed { .. }));
}

#[test]
fn a_probe_that_outlives_its_deadline_is_a_bound_and_not_a_failed_probe() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.time_out("-m");

    let error = list_php_extensions(&host, distro(), &version()).unwrap_err();

    assert!(matches!(
        error,
        PhpOpError::Bound(BoundExceeded::TimedOut { .. })
    ));
}

#[test]
fn a_version_that_is_not_installed_is_refused_before_anything_runs() {
    let host = FakePhpHost::empty();

    let error = list_php_extensions(&host, distro(), &version()).unwrap_err();

    assert!(
        matches!(error, PhpOpError::PhpVersionNotInstalled { ref version } if version == "8.3")
    );
    assert!(host.commands().is_empty());
}

#[test]
fn a_version_outside_the_supported_set_is_refused_before_anything_runs() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    let error =
        list_php_extensions(&host, distro(), &PhpVersion::parse("9.9").unwrap()).unwrap_err();

    assert!(matches!(error, PhpOpError::UnsupportedVersion { .. }));
    assert!(host.commands().is_empty());
}
