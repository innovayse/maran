//! The paths and the unit name one account's jail is made of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::{AccountJail, escape_path};

/// The unit directory every test here writes into.
const UNIT_DIRECTORY: &str = "/etc/systemd/system";

/// The jail sits beside the home, never inside it.
#[test]
fn a_jail_is_derived_from_the_account_and_never_from_a_request() {
    let account = AccountName::parse("alice").expect("valid");

    let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.directory(), "/var/lib/maran/sftp/alice");
    assert_eq!(jail.mount_point(), "/var/lib/maran/sftp/alice/home");
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

    assert_eq!(jail.unit_name(), "var-lib-maran-sftp-alice-home.mount");
    assert_eq!(
        jail.unit_path(),
        "/etc/systemd/system/var-lib-maran-sftp-alice-home.mount"
    );
}

/// An account name with an underscore keeps it, as systemd does.
#[test]
fn an_underscore_in_an_account_name_survives_the_escaping_unchanged() {
    let account = AccountName::parse("alice_two").expect("valid");

    let jail = AccountJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.unit_name(), "var-lib-maran-sftp-alice_two-home.mount");
}

/// Anything systemd would escape is escaped, not passed through.
#[test]
fn a_character_systemd_escapes_becomes_its_hexadecimal_form() {
    // No path this area builds contains one today. The full rule is implemented
    // anyway, so the unit name does not quietly depend on the alphabet of a
    // validator in another crate.
    assert_eq!(escape_path("/var/lib/a b"), "var-lib-a\\x20b");
    assert_eq!(escape_path("/var/lib/a-b"), "var-lib-a\\x2db");
    assert_eq!(escape_path("/var/lib/a.b_c"), "var-lib-a.b_c");
}
