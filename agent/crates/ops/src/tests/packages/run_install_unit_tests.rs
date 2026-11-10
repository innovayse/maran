//! Tests for the two unit starters: which unit each names, and that a removal is not an
//! install.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::utils::call_class::CallClass;

use super::{run_install_unit, run_remove_unit};
use crate::packages::PackageError;
use crate::php::fake_php_host::{FakePhpHost, distro};

#[test]
fn the_install_starter_starts_the_install_unit_of_the_package() {
    let host = FakePhpHost::empty();

    let unit = run_install_unit(&host, distro(), "apache2").unwrap();

    assert_eq!(unit, "maran-app-install@apache2.service");
    let calls = host.commands();
    assert_eq!(calls.len(), 1);
    assert_eq!(
        calls[0][1..],
        ["start", "--", "maran-app-install@apache2.service"]
    );
    assert_eq!(host.classes(), [CallClass::PackageInstall]);
}

#[test]
fn the_remove_starter_starts_the_removal_unit_and_never_the_install_unit() {
    let host = FakePhpHost::empty();

    let unit = run_remove_unit(&host, distro(), "apache2").unwrap();

    assert_eq!(unit, "maran-app-remove@apache2.service");
    assert!(
        host.commands()
            .iter()
            .all(|call| !call.iter().any(|part| part.contains("app-install")))
    );
}

#[test]
fn a_unit_that_exits_non_zero_is_a_failure_naming_the_unit() {
    let host = FakePhpHost::empty();
    host.answer("start", 1, "", "Job failed");
    host.answer("show", 1, "", "");

    match run_remove_unit(&host, distro(), "apache2") {
        Err(PackageError::PackageManager { stderr }) => {
            assert!(stderr.contains("maran-app-remove@apache2.service"));
        }
        other => panic!("expected PackageManager, got {other:?}"),
    }
}

#[test]
fn a_unit_that_outlives_its_deadline_is_a_bound_and_not_a_failure() {
    let host = FakePhpHost::empty();
    host.time_out("start");

    assert!(matches!(
        run_install_unit(&host, distro(), "apache2"),
        Err(PackageError::Bound(_))
    ));
}
