//! Asking the password database which of an account's logins still work.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::sftp::fake_sftp_host::{FakeSftpHost, account, distro};
use crate::sftp::inspect_account_logins::inspect_account_logins;
use crate::sftp::set_account_logins_locked::set_account_logins_locked;
use crate::sftp::sftp_error::SftpError;

/// A host holding two of `alice`'s logins, neither of them locked.
fn two_logins() -> FakeSftpHost {
    FakeSftpHost::new()
        .with_login("alice_web")
        .with_login("alice_deploy")
}

#[test]
fn a_login_that_still_authenticates_is_reported_as_unlocked() {
    // The inverse control for everything below. This is also the DEFECT's own
    // shape: before the operation existed, this is what every login of a
    // suspended account looked like.
    let host = two_logins();

    let facts = inspect_account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(facts.len(), 2);
    assert!(facts.iter().all(|fact| !fact.locked));
}

#[test]
fn a_locked_login_is_reported_as_locked_and_named() {
    let host = two_logins();
    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    let facts = inspect_account_logins(&host, distro(), &account()).expect("observed");

    assert!(facts.iter().all(|fact| fact.locked));
    let mut names: Vec<&str> = facts.iter().map(|fact| fact.user.as_str()).collect();
    names.sort_unstable();
    assert_eq!(names, ["alice_deploy", "alice_web"]);
}

#[test]
fn each_login_is_asked_about_by_name_and_never_inferred_from_a_neighbour() {
    // The state of one login says nothing about the state of another. An
    // implementation that asked once and reported the answer for all of them
    // passes every assertion that locks or unlocks the whole set.
    let host = two_logins().with_locked("alice_web");

    let facts = inspect_account_logins(&host, distro(), &account()).expect("observed");

    let web = facts
        .iter()
        .find(|fact| fact.user.as_str() == "alice_web")
        .expect("the locked login is reported");
    let deploy = facts
        .iter()
        .find(|fact| fact.user.as_str() == "alice_deploy")
        .expect("the open login is reported");
    assert!(web.locked);
    assert!(
        !deploy.locked,
        "the open login must not inherit the other's state"
    );
}

#[test]
fn a_login_with_no_password_stays_locked_after_a_resume_and_is_reported_as_such() {
    // The one measured asymmetry of the real tool, reported rather than hidden:
    // `usermod --unlock` refuses a passwordless login while exiting zero.
    let host = two_logins().with_passwordless("alice_deploy");
    set_account_logins_locked(&host, distro(), &account(), true).expect("locked");

    set_account_logins_locked(&host, distro(), &account(), false).expect("resumed");
    let facts = inspect_account_logins(&host, distro(), &account()).expect("observed");

    let deploy = facts
        .iter()
        .find(|fact| fact.user.as_str() == "alice_deploy")
        .expect("the login is reported");
    assert!(
        deploy.locked,
        "the panel must be able to SEE that this login did not come back"
    );
}

/// The RHEL family's own spelling of "locked" is recognised, and its spelling
/// of "usable" is not mistaken for one.
///
/// `passwd -S` prints `L`/`P` on the Debian family and `LK`/`PS` on the RHEL
/// one — measured on both polygon images, not assumed. An exact comparison
/// against `"L"` reported every RHEL login as still authenticating, which is
/// the direction that refuses a suspension the host had actually performed.
#[test]
fn the_rhel_familys_own_spellings_of_the_password_states_are_read_correctly() {
    // One login only: `passwd_prints` answers the same line for every question,
    // and a line naming a different login is REFUSED by design.
    let locked = FakeSftpHost::new().with_login("alice_web");
    locked.passwd_prints("alice_web LK 2026-09-08 0 99999 7 -1\n");
    let usable = FakeSftpHost::new().with_login("alice_web");
    usable.passwd_prints("alice_web PS 2026-09-08 0 99999 7 -1\n");

    let observed_locked = inspect_account_logins(&locked, distro(), &account())
        .expect("observed")
        .into_iter()
        .find(|fact| fact.user.as_str() == "alice_web")
        .expect("the login is reported");
    let observed_usable = inspect_account_logins(&usable, distro(), &account())
        .expect("observed")
        .into_iter()
        .find(|fact| fact.user.as_str() == "alice_web")
        .expect("the login is reported");

    assert!(observed_locked.locked, "`LK` is locked on the RHEL family");
    assert!(
        !observed_usable.locked,
        "`PS` is a usable password, not a locked one"
    );
}

#[test]
fn an_account_with_no_logins_is_an_observed_empty_answer() {
    let host = FakeSftpHost::new();

    let facts = inspect_account_logins(&host, distro(), &account()).expect("observed");

    assert!(facts.is_empty());
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_instead_of_answering_empty() {
    let host = two_logins();
    host.forget_the_account();

    let refused = inspect_account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(SftpError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn a_passwd_that_refuses_is_reported_and_not_read_as_unlocked() {
    let host = two_logins();
    host.refuse_passwd_with(1);

    let refused = inspect_account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(SftpError::SpawnFailed { code: 1 })),
        "got {refused:?}"
    );
}

#[test]
fn a_status_line_about_a_different_login_is_refused_rather_than_believed() {
    // `passwd -S` with no argument prints the CALLER's own account. Reading the
    // second letter of whatever came back would report root's state as a
    // customer's, and "not locked" is the direction that certifies nothing.
    let host = two_logins();
    host.passwd_prints("root L 2026-09-08 0 99999 7 -1\n");

    let refused = inspect_account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(SftpError::StatusUnreadable)),
        "got {refused:?}"
    );
}
