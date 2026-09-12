//! Tests for [`RollbackGuard`], and for the one thing it now refuses to do.
//!
//! Tests mirror the source tree under `src/tests/` instead of sitting inside
//! the unit they exercise (rules/testing.md).

// A failing assertion IS the reporting mechanism for a test, so the workspace-wide
// bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::safe_write::RollbackGuard;

/// What a target held before the operation this guard belongs to.
const PREVIOUS: &str = "server { server_name previous.test; }\n";

/// What that operation swapped in.
const SWAPPED_IN: &str = "server { server_name swapped-in.test; }\n";

/// What somebody else wrote over it afterwards.
const SOMEBODY_ELSES: &str = "server { server_name somebody-elses.test; }\n";

#[test]
fn a_rollback_puts_the_previous_content_back_when_the_target_is_still_ours() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SWAPPED_IN).expect("the fixture directory is writable");

    let mut guard = RollbackGuard::new(
        target.clone(),
        Some(PREVIOUS.as_bytes().to_vec()),
        Some(SWAPPED_IN.as_bytes().to_vec()),
    );

    guard.restore().expect("the restoration must succeed");

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        PREVIOUS
    );
}

#[test]
fn a_rollback_of_a_file_that_did_not_exist_before_removes_it() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SWAPPED_IN).expect("the fixture directory is writable");

    let mut guard = RollbackGuard::new(target.clone(), None, Some(SWAPPED_IN.as_bytes().to_vec()));

    guard.restore().expect("the restoration must succeed");

    assert!(
        !target.exists(),
        "a target that was created must be removed"
    );
}

#[test]
fn a_rollback_does_not_overwrite_a_file_another_operation_has_since_changed() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SOMEBODY_ELSES).expect("the fixture directory is writable");

    let mut guard = RollbackGuard::new(
        target.clone(),
        Some(PREVIOUS.as_bytes().to_vec()),
        Some(SWAPPED_IN.as_bytes().to_vec()),
    );

    guard
        .restore()
        .expect("a refused restoration is not an error");

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        SOMEBODY_ELSES,
        "a rollback must not undo a change it knows nothing about"
    );
}

#[test]
fn a_rollback_does_not_recreate_a_file_another_operation_has_since_deleted() {
    // C-1 interleaving 3, at the guard: a certificate installation captured a
    // site's vhost, a concurrent deletion committed, and the installation then
    // failed. The blind `fs::write` this replaces put the deleted site's vhost
    // back — live, unowned, and removable by no rpc.
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");

    let mut guard = RollbackGuard::new(
        target.clone(),
        Some(PREVIOUS.as_bytes().to_vec()),
        Some(SWAPPED_IN.as_bytes().to_vec()),
    );

    guard
        .restore()
        .expect("a refused restoration is not an error");

    assert!(
        !target.exists(),
        "a rollback must not recreate a configuration another operation deleted"
    );
}

#[test]
fn a_removals_rollback_puts_the_file_back_only_while_it_is_still_absent() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");

    // A removal swaps in nothing, so the state it requires to find is an
    // absent file — which is what it left.
    let mut guard = RollbackGuard::new(target.clone(), Some(PREVIOUS.as_bytes().to_vec()), None);
    guard.restore().expect("the restoration must succeed");

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        PREVIOUS
    );
}

#[test]
fn a_removals_rollback_does_not_overwrite_a_file_another_operation_has_since_created() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SOMEBODY_ELSES).expect("the fixture directory is writable");

    let mut guard = RollbackGuard::new(target.clone(), Some(PREVIOUS.as_bytes().to_vec()), None);
    guard
        .restore()
        .expect("a refused restoration is not an error");

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        SOMEBODY_ELSES,
        "a removal's rollback must not overwrite a file created after it"
    );
}

#[test]
fn a_committed_guard_leaves_the_target_alone_when_it_is_dropped() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SWAPPED_IN).expect("the fixture directory is writable");

    let guard = RollbackGuard::new(
        target.clone(),
        Some(PREVIOUS.as_bytes().to_vec()),
        Some(SWAPPED_IN.as_bytes().to_vec()),
    );
    guard.commit();

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        SWAPPED_IN
    );
}

#[test]
fn an_uncommitted_guard_restores_when_it_is_dropped() {
    let directory = tempfile::tempdir().expect("a temporary directory");
    let target = directory.path().join("site.conf");
    std::fs::write(&target, SWAPPED_IN).expect("the fixture directory is writable");

    drop(RollbackGuard::new(
        target.clone(),
        Some(PREVIOUS.as_bytes().to_vec()),
        Some(SWAPPED_IN.as_bytes().to_vec()),
    ));

    assert_eq!(
        std::fs::read_to_string(&target).expect("the target is readable"),
        PREVIOUS
    );
}
