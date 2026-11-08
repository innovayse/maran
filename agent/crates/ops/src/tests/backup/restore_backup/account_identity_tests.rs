//! The account's identity, asked again before a restore swaps the home.

use crate::backup::model::account_identity::AccountIdentity;

use super::super::*;
use super::restore_fixture::{Fixture, databases, host_with, run, sink};

/// The account's identity, asked again immediately before the swap, is what
/// decides — not the pair read when the operation started.
#[test]
fn a_restore_whose_account_uid_moved_refuses_before_it_touches_the_home() {
    // C-4. `AccountIds` used to be resolved once, at the top, and carried in
    // `RestorePlacement` for the whole operation — hours, for a large account. A
    // `DeleteAccount` landing between the two renames made `userdel --remove`
    // succeed and the second rename CREATE the home, chowned to that
    // remembered uid — which `useradd` gives to the next account made on the
    // host, so one customer's files end up under another customer's identity
    // and the agent's own ownership check agrees they belong there.
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    let moved = AccountIdentity {
        uid: fixture.identity().uid + 1,
        gid: fixture.identity().gid,
    };
    // One answer, not two: these tests drive `restore_in`, and the read the
    // ENTRY POINT does before building the placement is outside it. So the
    // single answer here IS the re-read taken immediately before the swap, and
    // it disagrees with the ids the placement was built from — which is
    // exactly the state a `userdel` during a long restore produces.
    host.answers_identities(&[Some(moved)]);

    let error = run(&fixture, &host, &databases(1), &mut sink()).expect_err("must refuse");

    assert!(
        matches!(error, BackupError::AccountIdentityChanged),
        "expected AccountIdentityChanged, got {error:?}"
    );
    // Before the first rename, which is the recoverable side of the line: the
    // home the account had is still the home it has.
    assert!(
        fixture.home().join("live.txt").is_file(),
        "the account's own home must be untouched"
    );
    assert!(
        !fixture.previous().exists(),
        "nothing must have been parked: the swap never started"
    );
    assert!(
        !fixture.staging().exists(),
        "the staging tree is cleaned up"
    );
}

/// The same window, entered by the account going away rather than by its
/// number moving.
#[test]
fn a_restore_whose_account_vanished_refuses_before_it_touches_the_home() {
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);
    host.answers_identities(&[None]);

    let error = run(&fixture, &host, &databases(1), &mut sink()).expect_err("must refuse");

    assert!(
        matches!(error, BackupError::ExtractionIdentityUnavailable),
        "expected ExtractionIdentityUnavailable, got {error:?}"
    );
    assert!(
        fixture.home().join("live.txt").is_file(),
        "the account's own home must be untouched"
    );
}

/// The positive control for both refusals above: the identity really is asked
/// a second time, and a restore whose answer did not move really does swap.
#[test]
fn the_accounts_identity_is_asked_again_before_the_home_is_swapped() {
    // Without this, an operation that had stopped asking would pass both
    // refusals above by refusing nothing at all — and a check that is never
    // reached refuses nothing, which is what makes this control the thing that
    // gives the other two their meaning.
    //
    // UNOBSERVED HERE: the entry point's own first read. These tests drive
    // `restore_in`, and `restore_backup` — which resolves the ids, builds the
    // `RestorePlacement` and takes the account lock — is one statement above it. What
    // this file can see is that the second read happens and that a disagreement
    // between the two refuses.
    let fixture = Fixture::new(1);
    let host = host_with(&fixture, 1);

    let outcome = run(&fixture, &host, &databases(1), &mut sink()).expect("the restore succeeds");

    assert_eq!(
        host.identity_calls(),
        1,
        "the re-read immediately before the swap must really happen"
    );
    assert!(outcome.files_restored);
    assert!(fixture.home().join("public").join("index.php").is_file());
}
