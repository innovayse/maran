//! The paths and the unit name one account's jail is made of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::AccountJail;

/// The unit directory every test here writes into.
const UNIT_DIRECTORY: &str = "/etc/systemd/system";

/// The jail sits beside the home, never inside it.
#[test]
fn a_jail_is_derived_from_the_account_and_never_from_a_request() {
    let account = AccountName::parse("alice").expect("valid");

    let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.directory(), "/var/lib/maran-sftp/alice");
    assert_eq!(jail.mount_point(), "/var/lib/maran-sftp/alice/home");
    assert_eq!(jail.source_directory(), "/home/alice");
}

/// systemd derives a mount unit's name from its mount point, so this does too.
#[test]
fn the_mount_unit_is_named_as_systemd_escapes_its_own_mount_point() {
    // systemd refuses to load a `.mount` unit whose file name is not the
    // escaping of its `Where=`. A friendlier name would fail on the host, at
    // load time, and never in a build.
    let account = AccountName::parse("alice").expect("valid");

    let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.unit_name(), "var-lib-maran\\x2dsftp-alice-home.mount");
    assert_eq!(
        jail.unit_path(),
        "/etc/systemd/system/var-lib-maran\\x2dsftp-alice-home.mount"
    );
}

/// An account name with an underscore keeps it, as systemd does.
#[test]
fn an_underscore_in_an_account_name_survives_the_escaping_unchanged() {
    let account = AccountName::parse("alice_two").expect("valid");

    let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(
        jail.unit_name(),
        "var-lib-maran\\x2dsftp-alice_two-home.mount"
    );
}

/// The whole derivation is unchanged by the escaping rule moving to `logins`.
#[test]
fn every_jail_value_is_byte_for_byte_what_it_was_before_the_escaping_rule_moved() {
    // Frozen from this same code BEFORE `escape_path` left this file for
    // `logins::systemd_escape`: each row was printed by the pre-refactor
    // implementation and pasted here unedited. The refactor's promise is that
    // the SFTP side does not move a byte, and this is the row-by-row form of
    // that promise — a promise no diff can make, because a diff cannot say what
    // the code produces.
    let expected = [
        (
            "alice",
            "/var/lib/maran-sftp/alice",
            "/var/lib/maran-sftp/alice/home",
            "var-lib-maran\\x2dsftp-alice-home.mount",
        ),
        (
            "abc",
            "/var/lib/maran-sftp/abc",
            "/var/lib/maran-sftp/abc/home",
            "var-lib-maran\\x2dsftp-abc-home.mount",
        ),
        (
            "a_b",
            "/var/lib/maran-sftp/a_b",
            "/var/lib/maran-sftp/a_b/home",
            "var-lib-maran\\x2dsftp-a_b-home.mount",
        ),
        (
            "a__b",
            "/var/lib/maran-sftp/a__b",
            "/var/lib/maran-sftp/a__b/home",
            "var-lib-maran\\x2dsftp-a__b-home.mount",
        ),
        (
            "user_1_2",
            "/var/lib/maran-sftp/user_1_2",
            "/var/lib/maran-sftp/user_1_2/home",
            "var-lib-maran\\x2dsftp-user_1_2-home.mount",
        ),
        (
            "z9_",
            "/var/lib/maran-sftp/z9_",
            "/var/lib/maran-sftp/z9_/home",
            "var-lib-maran\\x2dsftp-z9_-home.mount",
        ),
        (
            "abcdefghij_klmnopqrst_uvwxyz12",
            "/var/lib/maran-sftp/abcdefghij_klmnopqrst_uvwxyz12",
            "/var/lib/maran-sftp/abcdefghij_klmnopqrst_uvwxyz12/home",
            "var-lib-maran\\x2dsftp-abcdefghij_klmnopqrst_uvwxyz12-home.mount",
        ),
    ];

    for (candidate, directory, mount_point, unit_name) in expected {
        let account = AccountName::parse(candidate).expect("valid");

        let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

        assert_eq!(jail.directory(), directory, "directory for {candidate:?}");
        assert_eq!(
            jail.mount_point(),
            mount_point,
            "mount point for {candidate:?}"
        );
        assert_eq!(jail.unit_name(), unit_name, "unit name for {candidate:?}");
    }
}
