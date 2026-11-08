//! Tests for [`reconcile_opcache_drop_ins`]: every installed version is
//! converged, and one version's refusal never stops the others.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::path::PathBuf;

use maran_agent_core::validation::web::php_version::PhpVersion;

use crate::php::PhpOpError;
use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::reconcile_opcache_drop_ins::reconcile_opcache_drop_ins;

/// Where the default adapter keeps the drop-in of `version`.
fn target(version: &str) -> PathBuf {
    PathBuf::from(format!(
        "{}/99-maran-opcache.ini",
        distro().php_ini_drop_in_directory(&PhpVersion::parse(version).unwrap())
    ))
}

#[test]
fn a_host_with_no_php_yields_no_outcome_and_writes_nothing() {
    let host = FakePhpHost::empty();

    assert!(reconcile_opcache_drop_ins(&host, distro()).is_empty());
    assert_eq!(host.writes(), 0);
}

#[test]
fn every_installed_version_gets_its_drop_in_newest_first() {
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);

    let outcomes = reconcile_opcache_drop_ins(&host, distro());

    let versions: Vec<&str> = outcomes
        .iter()
        .map(|outcome| outcome.version.as_str())
        .collect();
    assert_eq!(versions, ["8.3", "8.1"]);
    assert!(outcomes.iter().all(|outcome| outcome.result.is_ok()));
    assert!(host.config(&target("8.3")).is_some());
    assert!(host.config(&target("8.1")).is_some());
    assert_eq!(host.writes(), 2);
}

#[test]
fn a_second_pass_over_current_files_writes_nothing() {
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);
    let _ = reconcile_opcache_drop_ins(&host, distro());

    let outcomes = reconcile_opcache_drop_ins(&host, distro());

    assert!(outcomes.iter().all(|outcome| outcome.result.is_ok()));
    assert_eq!(host.writes(), 2);
}

#[test]
fn one_versions_refusal_is_reported_and_the_others_are_still_converged() {
    // The fake's validator refuses every write, so both versions fail, but the
    // second is still attempted: two outcomes, both carrying the refusal.
    let host = FakePhpHost::with_installed(&["8.1", "8.3"]);
    host.reject_validation("bad ini");

    let outcomes = reconcile_opcache_drop_ins(&host, distro());

    assert_eq!(outcomes.len(), 2);
    for outcome in &outcomes {
        match &outcome.result {
            Err(PhpOpError::PoolValidation { stderr }) => assert_eq!(stderr, "bad ini"),
            other => panic!("expected PoolValidation, got {other:?}"),
        }
    }
    assert_eq!(host.writes(), 2);
}
