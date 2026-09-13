//! Tests for the startup pass over the php-fpm pool trees.
//!
//! Test code may panic where production code may not (rules/rust.md
//! "Toolchain & lints"): a failed unwrap here is the test reporting, and the
//! workspace lints are denied rather than warned, so the exemption is declared
//! file by file as every other test file in this tree declares it.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::php::fake_php_host::{FakePhpHost, distro};
use crate::php::model::pool_tree_decision::PoolTreeDecision;
use crate::php::reload_pool_trees;

/// A host with no PHP installed has no pool tree, and the pass says so by
/// answering nothing rather than by inventing a verdict about a directory that
/// is not there.
#[test]
fn a_host_with_no_php_installed_yields_no_outcome() {
    let host = FakePhpHost::empty();

    assert_eq!(reload_pool_trees(&host, distro()), Vec::new());
    assert_eq!(host.commands(), Vec::<Vec<String>>::new());
}

/// One outcome per installed version, newest first, and every one of them
/// applied: this is the inverse control for the refusal below, and the shape
/// that proves the pass is per version rather than per host.
#[test]
fn every_installed_version_is_validated_and_reloaded_on_its_own() {
    let host = FakePhpHost::with_installed(&["8.2", "8.3"]);

    let outcomes = reload_pool_trees(&host, distro());

    let versions: Vec<String> = outcomes
        .iter()
        .map(|outcome| outcome.version.clone())
        .collect();
    assert_eq!(versions, vec!["8.3".to_owned(), "8.2".to_owned()]);
    for outcome in &outcomes {
        assert_eq!(outcome.decision, PoolTreeDecision::Applied);
        assert_eq!(outcome.stderr, "");
    }
}

/// The pass asks php-fpm's OWN validator, from the adapter, with the
/// validate-only flag, and reloads that version's OWN service — it invents
/// neither a check nor a service name.
///
/// UNOBSERVED HERE: whether the adapter spells the binary and the service
/// correctly. The expectations are read from the same adapter the code reads
/// them from, so this pins the SEAM — that the startup question is the one
/// every individual pool write asks — and not the values. The flag and the
/// subcommand are literals, so a pass that stopped asking a validate-only
/// question, or reloaded something else, would be caught.
#[test]
fn the_pass_spawns_the_versions_own_validator_and_then_its_own_service_reload() {
    let host = FakePhpHost::with_installed(&["8.3"]);

    let _outcomes = reload_pool_trees(&host, distro());

    assert_eq!(
        host.commands(),
        vec![
            vec![distro().php_fpm_binary("8.3"), "-t".to_owned()],
            vec![
                distro().service_manager().to_owned(),
                "reload".to_owned(),
                distro().php_fpm_service("8.3"),
            ],
        ]
    );
}

/// A rejected tree is reported as rejected, carries the validator's own reason,
/// and is NOT reloaded — which is the whole point: reloading a tree php-fpm has
/// refused would be asking a live service to load a file that cannot be parsed.
#[test]
fn a_rejected_pool_tree_is_reported_with_its_reason_and_not_reloaded() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.reject_validation("unexpected end of file in /etc/php/8.3/fpm/pool.d/acme.conf");

    let outcomes = reload_pool_trees(&host, distro());

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].decision, PoolTreeDecision::Refused);
    assert_eq!(
        outcomes[0].stderr,
        "unexpected end of file in /etc/php/8.3/fpm/pool.d/acme.conf"
    );
    assert_eq!(
        host.commands(),
        vec![vec![distro().php_fpm_binary("8.3"), "-t".to_owned()]],
        "a tree the validator refused must not be reloaded onto the running service"
    );
}

/// One version's broken tree does not stop the others being observed. The
/// fake's validator answers for the whole host, so this is asserted the only
/// way it can be honestly: both versions are still reported, each with its own
/// outcome, rather than the pass returning at the first refusal.
#[test]
fn one_versions_refusal_does_not_stop_the_other_versions_being_observed() {
    let host = FakePhpHost::with_installed(&["8.2", "8.3"]);
    host.reject_validation("syntax error");

    let outcomes = reload_pool_trees(&host, distro());

    assert_eq!(outcomes.len(), 2);
    for outcome in &outcomes {
        assert_eq!(outcome.decision, PoolTreeDecision::Refused);
    }
}

/// A tree that validates and a reload that then refuses is a DIFFERENT job for
/// an operator — usually a php-fpm that is not running — and is reported as its
/// own outcome instead of being folded into a rejected tree.
#[test]
fn a_reload_that_refuses_on_a_valid_tree_is_reported_apart_from_a_rejection() {
    let host = FakePhpHost::with_installed(&["8.3"]);
    host.reject_commands("Unit php8.3-fpm.service not loaded.");

    let outcomes = reload_pool_trees(&host, distro());

    assert_eq!(outcomes.len(), 1);
    assert_eq!(outcomes[0].decision, PoolTreeDecision::NotReloaded);
    assert!(
        outcomes[0].stderr.contains("not loaded"),
        "the reload's own reason must reach the operator: {}",
        outcomes[0].stderr
    );
}

/// The pass writes NOTHING. It is the write protocol with the write taken out,
/// and a version of it that touched a pool file would be a startup service
/// editing customer configuration with nobody having asked.
#[test]
fn the_pass_writes_no_pool_file() {
    let host = FakePhpHost::with_installed(&["8.2", "8.3"]);

    let _outcomes = reload_pool_trees(&host, distro());

    assert_eq!(host.writes(), 0);
    assert_eq!(host.removals(), 0);
}
