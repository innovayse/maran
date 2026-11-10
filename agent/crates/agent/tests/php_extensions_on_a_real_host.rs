//! PHP extensions against a real php-fpm (issue #157).
//!
//! `list_php_extensions` reads what the daemon loads from `php-fpm<version> -m`
//! and `set_php_extension` switches an extension with the family's own tool and
//! checks the result with the real `php-fpm -t`. Both are only worth anything
//! against the real binary: the listing's shape (section headers, a module name
//! with a space in it, `mysqli` for the MySQL driver) is what the unit tests
//! can only assume.
//!
//! Three claims, each answered by the real php-fpm and not by the code under
//! test:
//!
//! 1. the entries of the list agree with a listing this file reads ITSELF, by
//!    running `php-fpm -m` and parsing it independently, with a positive control
//!    that the independent reading found modules at all and a mix of loaded and
//!    absent extensions, so an all-true or all-false answer cannot pass;
//! 2. on the Debian family an extension that is loaded can be switched off and
//!    on again, the real `php-fpm -m` agrees after each step and the real
//!    `php-fpm -t` accepts the tree; on the RHEL family, which has no clean
//!    switch, switching an extension off is refused and the listing is
//!    unchanged;
//! 3. a version that is supported but not installed is refused with the typed
//!    error and not an empty list.
//!
//! What this suite CANNOT see, stated rather than implied: the polygon's
//! `systemctl` is a stand-in, so the install unit a switch-on starts installs
//! nothing (the package is already on the image) and the reload does not model
//! `ExecReload=`. It therefore proves the switch, the validation and the
//! observation, and not a package arriving from a repository. The package names
//! the adapters derive are the author's reading and are not exercised here.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// This suite uses only part of the shared fixture; the other suites use the rest of
// it. The allow is on the module rather than on the fixture, so a fixture item no
// suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[allow(dead_code)]
#[path = "fixtures/polygon_php.rs"]
mod polygon_php;

use std::collections::BTreeSet;
use std::path::Path;

use maran_agent_core::validation::web::php_extension_name::PhpExtensionName;
use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, DistroFamily, adapter_for, detect};
use maran_ops::php::{
    ExtensionAvailability, PhpExtension, PhpOpError, ProcessPhpHost, list_php_extensions,
    set_php_extension,
};

use polygon_account::PolygonAccount;
use polygon_php::PolygonPhp;

/// Where `phpenmod` lives on the Debian family, spelled again here rather than
/// taken from the adapter: the cleanup guard below must work even when the
/// code under test is what broke.
const PHPENMOD: &str = "/usr/sbin/phpenmod";

/// The family of the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_family() -> DistroFamily {
    detect()
        .expect("a polygon image is a supported host")
        .family
}

/// The distribution adapter for the polygon this suite is running in.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(polygon_family())
}

/// The name `php-fpm -m` prints for a catalogue extension, spelled here and not
/// taken from `PhpExtensionName::loaded_module_name`, so a wrong mapping in the
/// code under test cannot also be the expectation.
fn printed_module_name(extension: &str) -> &str {
    match extension {
        "mysql" => "mysqli",
        "opcache" => "zend opcache",
        other => other,
    }
}

/// What the real `php-fpm<version> -m` prints, parsed by this file: lower-cased
/// module names without the section headers and blank lines.
///
/// # Panics
///
/// Panics when php-fpm cannot be run or exits non-zero.
fn real_modules() -> BTreeSet<String> {
    let binary = polygon_distro().php_fpm_binary(&PolygonPhp::version());
    let output = std::process::Command::new(&binary)
        .arg("-m")
        .output()
        .unwrap_or_else(|error| panic!("the polygon image installs {binary}: {error}"));
    assert!(
        output.status.success(),
        "php-fpm -m must succeed: {}",
        String::from_utf8_lossy(&output.stderr)
    );

    String::from_utf8_lossy(&output.stdout)
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('['))
        .map(str::to_lowercase)
        .collect()
}

/// The agent's reading of the polygon version's extensions.
///
/// # Panics
///
/// Panics when the listing fails.
fn listed() -> Vec<PhpExtension> {
    list_php_extensions(
        &ProcessPhpHost::new(),
        polygon_distro(),
        &PolygonPhp::version(),
    )
    .unwrap_or_else(|error| panic!("listing extensions must succeed in the polygon: {error}"))
}

/// Switches an extension back on with the family's own tool when dropped, so a
/// failing test cannot leave the shared php-fpm without a module the next test
/// expects. Plain `phpenmod`, not `set_php_extension`: cleanup must not route
/// through the operation under test.
struct ReenableOnDrop {
    /// The catalogue name to switch back on.
    module: String,
}

impl Drop for ReenableOnDrop {
    /// Runs `phpenmod -v <version> -s fpm <module>`, ignoring a failure because
    /// a panicking test is already reporting.
    fn drop(&mut self) {
        let _ = std::process::Command::new(PHPENMOD)
            .args(["-v", PolygonPhp::VERSION, "-s", "fpm", &self.module])
            .output();
    }
}

#[test]
#[ignore = "runs the real php-fpm -m: polygon only"]
fn the_extension_list_agrees_with_what_the_real_php_fpm_loads() {
    PolygonAccount::require_polygon();

    let modules = real_modules();
    // Positive control: the independent reading found modules, and php-fpm's
    // always-present core is among them. An empty set would make every
    // comparison below agree with an all-false answer.
    assert!(
        modules.contains("core"),
        "the independent reading found {modules:?}"
    );

    let extensions = listed();

    let names: Vec<&str> = extensions.iter().map(|item| item.name.as_str()).collect();
    assert_eq!(
        names,
        vec![
            "bcmath", "curl", "gd", "gmp", "imagick", "intl", "mbstring", "mysql", "opcache",
            "redis", "soap", "xml", "zip"
        ],
        "one entry per catalogue name, alphabetical"
    );
    for item in &extensions {
        let expected = modules.contains(printed_module_name(item.name.as_str()));
        assert_eq!(
            item.enabled,
            expected,
            "`{}`: the list says {}, the real php-fpm -m (read independently) says {}",
            item.name.as_str(),
            item.enabled,
            expected
        );
    }
    // The mix guard, on the axis that can go blind: a list that reported every
    // entry loaded, or none, would agree with a listing that was all of one kind.
    assert!(
        extensions.iter().any(|item| item.enabled),
        "the polygon's php-fpm must load at least one catalogue extension: {modules:?}"
    );
    assert!(
        extensions.iter().any(|item| !item.enabled),
        "the polygon's php-fpm must lack at least one catalogue extension: {modules:?}"
    );
}

#[test]
#[ignore = "runs the real php-fpm -m: polygon only"]
fn availability_follows_the_family_the_host_runs() {
    PolygonAccount::require_polygon();

    let extensions = listed();

    match polygon_family() {
        DistroFamily::Debian => assert!(
            extensions
                .iter()
                .all(|item| item.availability == ExtensionAvailability::Available),
            "the Debian family has a package and a switch for every catalogue entry"
        ),
        DistroFamily::Rhel => {
            // No clean switch is known for this family: nothing is `Available`,
            // `curl` has no package of its own, every other entry can only be
            // turned on.
            for item in &extensions {
                let expected = if item.name.as_str() == "curl" {
                    ExtensionAvailability::NotPackaged
                } else {
                    ExtensionAvailability::DisableNotSupported
                };
                assert_eq!(item.availability, expected, "`{}`", item.name.as_str());
            }
        }
        DistroFamily::Suse => {
            // UNVERIFIED: read from the distribution's `php8-<extension>` package naming and
            // from the absence of a `phpdismod`-style tool, not measured; confirm with
            // `rpm -ql php8-gd` and `command -v phpdismod` in the pinned Leap image. Every
            // catalogue entry has a package of its own (`curl` included, unlike the RHEL
            // family) and none has a switch, so every entry can only be turned on.
            for item in &extensions {
                assert_eq!(
                    item.availability,
                    ExtensionAvailability::DisableNotSupported,
                    "`{}`",
                    item.name.as_str()
                );
            }
        }
    }
}

#[test]
#[ignore = "switches a real php-fpm module and runs the real php-fpm -t: polygon only"]
fn switching_an_extension_is_observed_by_the_real_php_fpm_or_refused_where_the_family_cannot() {
    PolygonAccount::require_polygon();
    let host = ProcessPhpHost::new();
    let version = PolygonPhp::version();
    let loaded = listed()
        .into_iter()
        .find(|item| item.enabled)
        .expect("the polygon's php-fpm loads at least one catalogue extension");
    let name: PhpExtensionName = loaded.name;

    match polygon_family() {
        DistroFamily::Debian => {
            let _restore = ReenableOnDrop {
                module: printed_module_name(name.as_str()).to_owned(),
            };

            set_php_extension(&host, polygon_distro(), &version, name, false, |_, _| {})
                .unwrap_or_else(|error| {
                    panic!("switching `{}` off must succeed: {error}", name.as_str())
                });

            assert!(
                !real_modules().contains(printed_module_name(name.as_str())),
                "the real php-fpm -m must no longer list `{}`",
                name.as_str()
            );
            PolygonPhp::assert_tree_valid(
                polygon_distro(),
                "the tree after an extension was switched off",
            );

            set_php_extension(&host, polygon_distro(), &version, name, true, |_, _| {})
                .unwrap_or_else(|error| {
                    panic!("switching `{}` on must succeed: {error}", name.as_str())
                });

            assert!(
                real_modules().contains(printed_module_name(name.as_str())),
                "the real php-fpm -m must list `{}` again",
                name.as_str()
            );
            PolygonPhp::assert_tree_valid(
                polygon_distro(),
                "the tree after an extension was switched back on",
            );
        }
        DistroFamily::Rhel => {
            let refusal =
                set_php_extension(&host, polygon_distro(), &version, name, false, |_, _| {});

            assert!(
                matches!(refusal, Err(PhpOpError::ExtensionSwitchUnsupported { .. })),
                "the RHEL family has no clean switch off, got {refusal:?}"
            );
            assert!(
                real_modules().contains(printed_module_name(name.as_str())),
                "a refused switch-off must leave `{}` loaded",
                name.as_str()
            );
        }
        DistroFamily::Suse => {
            // UNVERIFIED: the family is read to ship no switch (see the availability test
            // above for the confirming commands), so the answer is the same typed refusal the
            // RHEL family gives and the loaded module is left alone.
            let refusal =
                set_php_extension(&host, polygon_distro(), &version, name, false, |_, _| {});

            assert!(
                matches!(refusal, Err(PhpOpError::ExtensionSwitchUnsupported { .. })),
                "the SUSE family has no clean switch off, got {refusal:?}"
            );
            assert!(
                real_modules().contains(printed_module_name(name.as_str())),
                "a refused switch-off must leave `{}` loaded",
                name.as_str()
            );
        }
    }
}

#[test]
#[ignore = "asks the real host which PHP versions are installed: polygon only"]
fn a_supported_version_that_is_not_installed_is_refused_and_not_listed_as_empty() {
    PolygonAccount::require_polygon();
    let absent = PhpVersion::parse("7.4").expect("7.4 is a supported version");
    let pools = polygon_distro().php_fpm_pool_directory(absent.as_str());
    assert!(
        !Path::new(&pools).exists(),
        "this test needs a version the image does not install, and {pools} exists"
    );

    let refusal = list_php_extensions(&ProcessPhpHost::new(), polygon_distro(), &absent);

    assert!(
        matches!(refusal, Err(PhpOpError::PhpVersionNotInstalled { .. })),
        "a version that is not installed must be refused, got {refusal:?}"
    );
}
