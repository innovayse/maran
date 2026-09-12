//! Asking the password database which credentials into an account's home exist.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::logins::account_logins::account_logins;
use crate::logins::fake_logins_host::{
    ACCOUNT_UID, FakeLoginsHost, OTHER_UID, account, account_row, distro,
};
use crate::logins::logins_error::LoginsError;
use crate::logins::model::login_protocol::LoginProtocol;

/// The jail `alice`'s SFTP logins are homed in, written out rather than derived,
/// so a change to either jail root fails here instead of silently agreeing with
/// itself.
const SFTP_JAIL: &str = "/var/lib/maran-sftp/alice";

/// The jail `alice`'s FTPS logins are homed in.
const FTPS_JAIL: &str = "/var/lib/maran-ftps/alice";

#[test]
fn an_ftps_login_is_found_and_labelled_by_the_jail_its_home_is() {
    // THE test this module exists for. The enumeration it replaces selected on
    // the SFTP jail alone, so `alice_files` was invisible to it — and an
    // assertion that "some logins were found" passes against exactly that
    // defect. So the assertion names WHICH logins, including the one whose home
    // is the other jail.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
        ("bob_web", OTHER_UID, "/var/lib/maran-sftp/bob"),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(
        observed
            .logins
            .iter()
            .map(|login| (login.name.as_str(), login.protocol))
            .collect::<Vec<_>>(),
        vec![
            ("alice_files", LoginProtocol::Ftps),
            ("alice_web", LoginProtocol::Sftp),
        ]
    );
    assert_eq!(
        observed.unmanaged, 0,
        "another account's login on another uid is not this account's business"
    );
}

#[test]
fn a_login_on_the_accounts_uid_that_is_in_neither_jail_is_counted() {
    // The positive control for the count: it is non-zero because a login
    // genuinely sits outside both jails, so a counter that returned a
    // hard-coded zero fails here rather than reading as "there was nothing
    // else".
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
        ("alice_hand", ACCOUNT_UID, "/home/alice"),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(
        observed.unmanaged, 1,
        "a hand-made login on this uid is COUNTED"
    );
    assert_eq!(
        observed
            .logins
            .iter()
            .map(|login| login.name.as_str())
            .collect::<Vec<_>>(),
        vec!["alice_files"],
        "and it is not reported as one of the panel's own logins"
    );
}

#[test]
fn the_accounts_own_entry_is_never_counted_as_a_login_it_does_not_manage() {
    // The account's own passwd row carries the account's uid and a home in
    // neither jail, which is the unmanaged predicate exactly. Counting it would
    // make every account on every host report one uncovered credential, and the
    // number would then mean nothing.
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(observed.unmanaged, 0);
    assert_eq!(observed.logins.len(), 1);
}

#[test]
fn a_login_on_a_different_uid_outside_both_jails_is_not_counted() {
    // The inverse control for the count. A count of "everything else on this
    // host" would be a number nobody could act on; it counts credentials that
    // write AS this account and nothing else.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("someone", OTHER_UID, "/home/someone"),
        ("service", OTHER_UID, "/var/lib/service"),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(observed.unmanaged, 0);
    assert!(observed.logins.is_empty());
}

#[test]
fn a_neighbouring_accounts_login_is_neither_reported_nor_counted() {
    // `alice_bob` is both a login of `alice` and a plausible account name of its
    // own. The classification tells them apart by the passwd home, and this is
    // the assertion that fails if anyone replaces it with a name prefix.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alicecorp", OTHER_UID, "/home/alicecorp"),
        ("alicecorp_web", OTHER_UID, "/var/lib/maran-sftp/alicecorp"),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(
        observed
            .logins
            .iter()
            .map(|login| login.name.as_str())
            .collect::<Vec<_>>(),
        vec!["alice_web"]
    );
    assert_eq!(observed.unmanaged, 0);
}

#[test]
fn a_row_in_this_accounts_jail_that_this_agent_could_not_have_named_is_counted_not_reported() {
    // A name that does not decode through the constructor that would have built
    // it is not a login this agent created, whatever directory it is homed in.
    // It is not reported — nothing is locked on a name this agent cannot
    // rebuild — and it is not silently dropped either.
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("intruder", ACCOUNT_UID, FTPS_JAIL)]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert!(observed.logins.is_empty());
    assert_eq!(observed.unmanaged, 1);
}

#[test]
fn a_login_that_still_authenticates_is_reported_as_unlocked() {
    // The inverse control for everything below. This is also the DEFECT's own
    // shape: before the operation existed, this is what every login of a
    // suspended account looked like.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert!(observed.logins.iter().all(|login| !login.locked));
}

#[test]
fn each_login_is_asked_about_by_name_and_never_inferred_from_a_neighbour() {
    // The state of one login says nothing about the state of another. An
    // implementation that asked once and reported the answer for all of them
    // passes every assertion that locks or unlocks the whole set — and it would
    // report an open FTPS login as locked because the SFTP one was.
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alice_files", ACCOUNT_UID, FTPS_JAIL),
    ])
    .with_locked("alice_web");

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    let web = observed
        .logins
        .iter()
        .find(|login| login.name == "alice_web")
        .expect("the locked login is reported");
    let files = observed
        .logins
        .iter()
        .find(|login| login.name == "alice_files")
        .expect("the open login is reported");
    assert!(web.locked);
    assert!(
        !files.locked,
        "the open login must not inherit the other's state"
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
    let locked =
        FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);
    locked.passwd_prints("alice_web LK 2026-09-09 0 99999 7 -1\n");
    let usable =
        FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);
    usable.passwd_prints("alice_web PS 2026-09-09 0 99999 7 -1\n");

    let observed_locked = account_logins(&locked, distro(), &account()).expect("observed");
    let observed_usable = account_logins(&usable, distro(), &account()).expect("observed");

    assert!(
        observed_locked.logins[0].locked,
        "`LK` is locked on the RHEL family"
    );
    assert!(
        !observed_usable.logins[0].locked,
        "`PS` is a usable password, not a locked one"
    );
}

#[test]
fn an_account_with_no_logins_is_an_observed_empty_answer() {
    let host = FakeLoginsHost::with_passwd(&[account_row()]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert!(observed.logins.is_empty());
    assert_eq!(observed.unmanaged, 0);
}

#[test]
fn a_password_database_that_cannot_be_read_refuses_instead_of_answering_empty() {
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);
    host.refuse_to_be_read();

    let refused = account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn a_database_holding_no_row_for_the_account_refuses_rather_than_counting_zero() {
    // The account's own row is where the uid the unmanaged count is measured
    // against comes from. Without it the count could only be a hard zero, which
    // is the blind answer that reads as "there was nothing else".
    let host = FakeLoginsHost::with_passwd(&[("alice_web", ACCOUNT_UID, SFTP_JAIL)]);

    let refused = account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::AccountMissing)),
        "got {refused:?}"
    );
}

#[test]
fn a_passwd_that_refuses_is_reported_and_not_read_as_unlocked() {
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);
    host.refuse_passwd_with(1);

    let refused = account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::SpawnFailed { code: 1 })),
        "got {refused:?}"
    );
}

#[test]
fn a_status_line_about_a_different_login_is_refused_rather_than_believed() {
    // `passwd -S` with no argument prints the CALLER's own account. Reading the
    // second field of whatever came back would report root's state as a
    // customer's, and "not locked" is the direction that certifies nothing.
    let host = FakeLoginsHost::with_passwd(&[account_row(), ("alice_web", ACCOUNT_UID, SFTP_JAIL)]);
    host.passwd_prints("root L 2026-09-09 0 99999 7 -1\n");

    let refused = account_logins(&host, distro(), &account());

    assert!(
        matches!(refused, Err(LoginsError::StatusUnreadable)),
        "got {refused:?}"
    );
}

/// A passwd row homed in this account's jail but owned by a DIFFERENT uid is
/// neither reported as one of the account's logins nor added to the count of
/// what the panel left uncovered.
///
/// The classification decides on the home before anything else, so without the
/// uid condition this row decodes as `alice`'s SFTP login: it would be named in
/// the suspension attestation and `usermod --lock`ed — a credential on somebody
/// else's uid, turned off by this panel — and unlocked again on reactivation.
///
/// It is not counted either. `unmanaged` means "shares this account's uid
/// without the panel having created it", which is the sentence an operator
/// reads on a suspension; a row that writes as somebody else would make that
/// sentence false.
///
/// **The account's own jailed row is in the same database on purpose.** A gate
/// mutated to refuse everything passes every test that only hands it rows it
/// must refuse, so the inverse control is inside this test: `alice_web` on the
/// account's uid, in the same jail, must still be reported. Both assertions
/// move if the uid condition is added, removed or inverted, which is exactly
/// the axis no other test in this file touches — every existing jailed case
/// holds the uid at `ACCOUNT_UID`.
#[test]
fn a_jailed_row_on_a_foreign_uid_is_neither_reported_nor_counted() {
    let host = FakeLoginsHost::with_passwd(&[
        account_row(),
        ("alice_web", ACCOUNT_UID, SFTP_JAIL),
        ("alice_stranger", OTHER_UID, SFTP_JAIL),
        ("alice_guest", OTHER_UID, FTPS_JAIL),
    ]);

    let observed = account_logins(&host, distro(), &account()).expect("observed");

    assert_eq!(
        observed
            .logins
            .iter()
            .map(|login| (login.name.as_str(), login.protocol))
            .collect::<Vec<_>>(),
        vec![("alice_web", LoginProtocol::Sftp)],
        "only the row that writes as this account is one of its logins"
    );
    assert_eq!(
        observed.unmanaged, 0,
        "a row on another uid is not what the unmanaged count means"
    );
}
