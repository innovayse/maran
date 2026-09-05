//! php-fpm pools against a real php-fpm, which is the only place `write_pool`'s
//! validation means anything.
//!
//! Both polygon images install php-fpm so that the agent's own validation can be
//! exercised, and until this file existed nothing exercised it: `write_pool`
//! chose a validator from `DistroAdapter::php_fpm_binary`, and no test had ever
//! run that binary — on AlmaLinux the path it named did not even exist, because
//! Remi ships a software collection rooted at `/opt/remi/php83/root` and the
//! adapter said `/usr/sbin/php-fpm83`. A pool write on that whole family failed
//! to spawn its own validator, and the first thing in the project that could
//! have caught it was this suite.
//!
//! The rejection here goes through `write_pool`, never through a `Validator` the
//! test assembles, so a pool operation that stopped validating would fail these
//! tests rather than pass them.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;

use std::path::{Path, PathBuf};

use maran_agent_core::validation::web::php_version::PhpVersion;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::php::{PhpOpError, PoolInput, PoolPaths, ProcessPhpHost, write_pool};

use polygon_account::PolygonAccount;
use polygon_config_file::PolygonConfigFile;

/// The PHP version both polygon images install: `8.3` on Sury, `83` on Remi.
const POLYGON_PHP_VERSION: &str = "8.3";

/// The argument that makes php-fpm check its configuration instead of serving.
///
/// Spelled again rather than imported, for the reason the nginx suite gives: a
/// test that took its expectation from the code under test would pass even if
/// that code stopped passing the argument.
const VALIDATE_ARGUMENT: &str = "-t";

/// A worker budget inside the range `write_pool` accepts.
const POLYGON_WORKERS: u32 = 4;

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The validated version this suite writes pools for.
///
/// # Panics
///
/// Panics if the constant above stops being a valid version.
fn polygon_version() -> PhpVersion {
    PhpVersion::parse(POLYGON_PHP_VERSION).expect("a valid PHP version")
}

#[test]
#[ignore = "writes a real php-fpm pool and runs the real php-fpm -t: polygon only"]
fn the_php_fpm_binary_the_adapter_names_exists_on_this_family() {
    PolygonAccount::require_polygon();

    // The cheapest test in the suite and the one that would have caught a whole
    // family's pools being unwritable. `write_pool` spawns this path as its
    // validator, so a path that is merely plausible turns every pool write on
    // that family into a failure to start the validator — which the operator
    // sees as "php-fpm validation failed" with nothing to fix.
    let binary = polygon_distro().php_fpm_binary(POLYGON_PHP_VERSION);
    assert!(
        Path::new(&binary).exists(),
        "the adapter names {binary} as this family's php-fpm and it is not there"
    );

    let pools = polygon_distro().php_fpm_pool_directory(POLYGON_PHP_VERSION);
    assert!(
        Path::new(&pools).is_dir(),
        "the adapter names {pools} as this family's pool directory and it is not there"
    );
}

#[test]
#[ignore = "writes a real php-fpm pool and runs the real php-fpm -t: polygon only"]
fn a_pool_is_written_and_the_real_php_fpm_accepts_it() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polypoolsone");
    let paths = PoolPaths::for_pool(polygon_distro(), account.name(), &polygon_version());
    let _pool = PolygonConfigFile::at(&paths.config_path);

    write_pool(
        &ProcessPhpHost::new(),
        polygon_distro(),
        &pool_for(&account),
    )
    .unwrap_or_else(|error| panic!("writing a pool must succeed in the polygon: {error}"));

    assert!(
        paths.config_path.exists(),
        "the pool must be on disk at {:?}",
        paths.config_path
    );

    let contents = std::fs::read_to_string(&paths.config_path).expect("the pool must be readable");
    assert!(
        contents.contains(&format!("user = {}", account.name().as_str())),
        "the pool must run as the account it belongs to"
    );
    assert!(
        contents.contains(&paths.socket_path.display().to_string()),
        "the pool must listen on the socket path the agent derived"
    );

    // The session directory belongs to the account, because it was created by a
    // child that had dropped to it — a root-owned one is unwritable by the
    // pool's workers, which is what sends PHP back to the shared /tmp.
    assert!(
        paths.session_directory.starts_with(account.home()),
        "a customer's PHP sessions belong inside that customer's home, never in a shared /tmp"
    );
    let sessions =
        std::fs::metadata(&paths.session_directory).expect("the session directory must exist");
    assert_eq!(
        std::os::unix::fs::MetadataExt::uid(&sessions),
        account.ids().uid()
    );

    // And the claim this suite exists for: the file the agent wrote is one the
    // real php-fpm parses, in the real pool directory, alongside every other
    // pool that directory holds.
    assert_valid_pool_tree("the pool the agent just wrote");
}

#[test]
#[ignore = "writes a real php-fpm pool and runs the real php-fpm -t: polygon only"]
fn a_pool_the_real_php_fpm_rejects_is_refused_by_write_pool_and_leaves_the_previous_one_in_place() {
    PolygonAccount::require_polygon();
    let account = PolygonAccount::create("polypoolstwo");
    let host = ProcessPhpHost::new();
    let paths = PoolPaths::for_pool(polygon_distro(), account.name(), &polygon_version());
    let _pool = PolygonConfigFile::at(&paths.config_path);

    write_pool(&host, polygon_distro(), &pool_for(&account))
        .unwrap_or_else(|error| panic!("the first pool must be written: {error}"));
    let good = std::fs::read_to_string(&paths.config_path).expect("the pool must be readable");

    // A neighbouring pool naming an account that does not exist. php-fpm reads
    // every file in the directory, so from this moment the tree is one the real
    // validator refuses — and the agent's next write has to notice, put its own
    // previous content back, and say so with a typed error rather than leaving a
    // half-applied change behind.
    let planted = PathBuf::from(polygon_distro().php_fpm_pool_directory(POLYGON_PHP_VERSION))
        .join("maran-polygon-planted.conf");
    let _planted = PolygonConfigFile::at(&planted);
    std::fs::write(
        &planted,
        "[maranpolygonplanted]\nlisten = /run/maran/php/planted.sock\n\
         pm = static\npm.max_children = 1\nuser = maranpolygonnosuchaccount\n",
    )
    .expect("the pool directory must be writable by root");

    // A different worker budget, so the rendered text really differs from what
    // is on disk: a rollback that did nothing at all would otherwise be
    // indistinguishable from a rollback that worked.
    let mut second = pool_for(&account);
    second.max_children = POLYGON_WORKERS + 1;
    let refusal = write_pool(&host, polygon_distro(), &second);

    assert!(
        matches!(refusal, Err(PhpOpError::PoolValidation { .. })),
        "write_pool must return the real php-fpm's refusal, got {refusal:?}"
    );

    let after = std::fs::read_to_string(&paths.config_path).expect("the pool must still be there");
    assert_eq!(
        after, good,
        "a rejected pool write must restore the previous pool byte for byte"
    );
    assert!(
        !after.contains(&format!("pm.max_children = {}", POLYGON_WORKERS + 1)),
        "not a byte of the rejected pool may remain"
    );

    // With the planted file gone the tree is valid again, which is what an
    // operator cares about: a failed write left a php-fpm that can still start.
    drop(_planted);
    assert_valid_pool_tree("the tree after a rejected pool write was rolled back");
}

/// The pool input this suite writes: the account, the polygon's PHP version, a
/// modest worker budget and no overrides.
fn pool_for(account: &PolygonAccount) -> PoolInput {
    PoolInput {
        account: account.name().clone(),
        version: polygon_version(),
        max_children: POLYGON_WORKERS,
        overrides: Vec::new(),
    }
}

/// Runs the real `php-fpm -t` and fails the test with php-fpm's own words when
/// it refuses.
///
/// # Panics
///
/// Panics when php-fpm cannot be run, or when it rejects the tree.
fn assert_valid_pool_tree(what: &str) {
    let binary = polygon_distro().php_fpm_binary(POLYGON_PHP_VERSION);
    let output = std::process::Command::new(&binary)
        .arg(VALIDATE_ARGUMENT)
        .output()
        .unwrap_or_else(|error| panic!("the polygon image installs {binary}: {error}"));

    assert!(
        output.status.success(),
        "php-fpm -t must accept {what}:\n{}",
        String::from_utf8_lossy(&output.stderr)
    );
}
