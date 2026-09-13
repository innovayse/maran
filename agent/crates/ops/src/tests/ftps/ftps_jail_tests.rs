//! The paths and the unit name one account's FTPS jail is made of.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::system::name::AccountName;

use super::FtpsJail;

/// The unit directory every test here writes into.
const UNIT_DIRECTORY: &str = "/etc/systemd/system";

/// The jail sits beside the home, never inside it.
#[test]
fn a_jail_is_derived_from_the_account_and_never_from_a_request() {
    let account = AccountName::parse("alice").expect("valid");

    let jail = FtpsJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.account(), "alice");
    assert_eq!(jail.directory(), "/var/lib/maran-ftps/alice");
    assert_eq!(jail.mount_point(), "/var/lib/maran-ftps/alice/home");
    assert_eq!(jail.source_directory(), "/home/alice");
}

/// systemd derives a mount unit's name from its mount point, so this does too.
#[test]
fn the_mount_unit_is_named_as_systemd_escapes_its_own_mount_point() {
    // systemd refuses to load a `.mount` unit whose file name is not the
    // escaping of its `Where=`. A friendlier `maran-ftps-alice.mount` would
    // fail on the host, at load time, and never in a build — the login would
    // land in an empty jail with nothing in the panel saying why.
    let account = AccountName::parse("alice").expect("valid");

    let jail = FtpsJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(jail.unit_name(), "var-lib-maran\\x2dftps-alice-home.mount");
    assert_eq!(
        jail.unit_path(),
        "/etc/systemd/system/var-lib-maran\\x2dftps-alice-home.mount"
    );
}

/// The `-` of the jail root's own name is not a separator and is escaped.
#[test]
fn the_dash_in_the_jail_root_is_escaped_and_the_separators_are_not() {
    // The exact collision the shared escaping rule exists for: `maran-ftps`
    // contains a `-` that is part of a NAME, while the `/` around it are
    // separators. A `replace('/', "-")` would render both as `-` and produce
    // `var-lib-maran-ftps-alice-home.mount`, which systemd derives from a
    // DIFFERENT path and therefore will not load for this one.
    let account = AccountName::parse("alice").expect("valid");

    let jail = FtpsJail::for_account(&account, UNIT_DIRECTORY);

    assert!(
        jail.unit_name().contains("maran\\x2dftps"),
        "the jail root's own dash must be escaped, got {:?}",
        jail.unit_name()
    );
    assert!(
        !jail.unit_name().contains("maran-ftps"),
        "an unescaped `maran-ftps` is a unit name systemd will not load, got {:?}",
        jail.unit_name()
    );
}

/// An account name with an underscore keeps it, as systemd does.
#[test]
fn an_underscore_in_an_account_name_survives_the_escaping_unchanged() {
    let account = AccountName::parse("alice_two").expect("valid");

    let jail = FtpsJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(
        jail.unit_name(),
        "var-lib-maran\\x2dftps-alice_two-home.mount"
    );
}

/// The FTPS jail is not the SFTP jail, and the two never resolve to one path.
#[test]
fn the_ftps_jail_never_collides_with_the_sftp_jail_of_the_same_account() {
    // Two protocols, two lifetimes: deleting the last login of one must not
    // unmount the other. That property starts here, with the two roots being
    // different directories and the two mount units different files.
    let account = AccountName::parse("alice").expect("valid");

    let jail = FtpsJail::for_account(&account, UNIT_DIRECTORY);

    assert_eq!(
        jail.directory(),
        format!("{}/alice", AgentPaths::FTPS_JAIL_ROOT)
    );
    assert_ne!(AgentPaths::FTPS_JAIL_ROOT, AgentPaths::SFTP_JAIL_ROOT);
    assert_eq!(
        jail.unit_name(),
        "var-lib-maran\\x2dftps-alice-home.mount",
        "the FTPS unit name must not be the SFTP one"
    );
}

/// The unit directory is the caller's; every other path is the agent's.
#[test]
fn the_unit_path_is_the_only_value_the_caller_contributes_to() {
    // Where a unit file lives is a fact of the service manager and arrives from
    // the `DistroAdapter`. Everything else is derived from `AgentPaths` and a
    // validated account, which is what leaves no caller-supplied path anywhere
    // in this type.
    let account = AccountName::parse("alice").expect("valid");

    let jail = FtpsJail::for_account(&account, "/usr/lib/systemd/system");

    assert_eq!(
        jail.unit_path(),
        "/usr/lib/systemd/system/var-lib-maran\\x2dftps-alice-home.mount"
    );
    assert_eq!(jail.directory(), "/var/lib/maran-ftps/alice");
}
