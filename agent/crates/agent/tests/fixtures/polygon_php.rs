//! The php-fpm facts two polygon suites share: the version both images install,
//! and the real validator that decides whether the host's pool tree is one
//! php-fpm will start on.
//!
//! Shared rather than copied, because the second copy of a block is the moment
//! it moves to a named home (rules/rust.md "The rule of two"). The pool suite
//! wants it to prove a pool it wrote is acceptable; the deletion suite wants it
//! to prove that a pool write racing a deletion did not leave a file that takes
//! php-fpm down for every tenant on the host. Two suites, one question.

use std::sync::Once;

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::DistroAdapter;

/// The argument that makes php-fpm check its configuration instead of serving.
///
/// Spelled again rather than imported from the agent, for the reason the nginx
/// suite gives: a test that took its expectation from the code under test would
/// pass even if that code stopped passing the argument.
const VALIDATE_ARGUMENT: &str = "-t";

/// The install unit the app store starts, spelled again here and not imported from the agent,
/// for the reason `VALIDATE_ARGUMENT` is: a test that took its expectation from the code under
/// test would pass even if that code started the wrong unit.
const INSTALL_UNIT_PREFIX: &str = "maran-app-install@";

/// Makes the install run once per test binary, however many tests ask.
static INSTALLED: Once = Once::new();

/// The real php-fpm of the polygon this suite is running in.
///
/// One unit rather than three loose functions (rules/rust.md, one public unit
/// per file): the version, the validator and the assertion are one subject —
/// what THIS host's php-fpm says about THIS host's pools.
pub struct PolygonPhp;

impl PolygonPhp {
    /// The PHP version the Debian and RHEL polygon images install: `8.3` on Sury, `83` on Remi.
    ///
    /// On the SUSE family this is a label and not the installed version: the family ships one
    /// distribution minor (8.4, UNVERIFIED) under unversioned paths, so its adapter answers the
    /// same binary, pool directory and unit for `8.3` as for `8.4`, and the suites that stat or
    /// write those answers do the same thing there. Confirm with `php-fpm -v` in the pinned Leap
    /// image.
    pub const VERSION: &'static str = "8.3";

    /// The validated version the polygon suites write pools for.
    ///
    /// # Panics
    ///
    /// Panics if the constant above stops being a valid version.
    pub fn version() -> PhpVersion {
        PhpVersion::parse(Self::VERSION).expect("a valid PHP version")
    }

    /// Installs this version's php-fpm through the installer's app unit, once per test
    /// binary, when the host does not have it yet.
    ///
    /// The polygon images preinstall no PHP (issue #517): a host starts with nothing for
    /// sites, and PHP arrives the way it does in production, as a package the root install
    /// unit installs. The package name is the adapter's, exactly as the agent's own install
    /// derives it, so a wrong adapter name fails here the same way it would fail an install.
    ///
    /// # Panics
    ///
    /// Panics when the unit cannot be started or its run fails.
    pub fn ensure_installed(distro: &dyn DistroAdapter) {
        INSTALLED.call_once(|| {
            let pool_directory = distro.php_fpm_pool_directory(Self::VERSION);
            if std::path::Path::new(&pool_directory).is_dir() {
                return;
            }
            let unit = format!(
                "{INSTALL_UNIT_PREFIX}{}.service",
                distro.php_package(Self::VERSION)
            );
            let status = std::process::Command::new(distro.service_manager())
                .args(["start", "--", unit.as_str()])
                .status()
                .unwrap_or_else(|error| panic!("could not start {unit}: {error}"));
            assert!(status.success(), "{unit} must install php-fpm: {status}");
        });
    }

    /// What the real `php-fpm -t` says about this host's whole pool tree right
    /// now: `None` when it accepts it, and its own words when it refuses.
    ///
    /// Returned rather than asserted, so a test can put php-fpm's answer INSIDE
    /// the message of the assertion that is really its subject — the pool file
    /// that should not be there — instead of making the reader run the
    /// validator by hand to find out whether the file matters.
    ///
    /// # Panics
    ///
    /// Panics when php-fpm cannot be run at all.
    pub fn tree_refusal(distro: &dyn DistroAdapter) -> Option<String> {
        let binary = distro.php_fpm_binary(&Self::version());
        let output = std::process::Command::new(&binary)
            .arg(VALIDATE_ARGUMENT)
            .output()
            .unwrap_or_else(|error| panic!("the polygon image installs {binary}: {error}"));

        if output.status.success() {
            return None;
        }

        Some(String::from_utf8_lossy(&output.stderr).into_owned())
    }

    /// Runs the real `php-fpm -t` and fails the test with php-fpm's own words
    /// when it refuses.
    ///
    /// This is the assertion that makes a stale pool a HOST-WIDE fact rather
    /// than one account's: the validator reads every file in the pool
    /// directory, so one pool naming a user who no longer resolves makes the
    /// master refuse to start or reload for every tenant on the machine.
    ///
    /// # Panics
    ///
    /// Panics when php-fpm cannot be run, or when it rejects the tree.
    pub fn assert_tree_valid(distro: &dyn DistroAdapter, what: &str) {
        if let Some(refusal) = Self::tree_refusal(distro) {
            panic!("php-fpm -t must accept {what}:\n{refusal}");
        }
    }
}
