//! The two names of a login's key file, and reading a login back out of one.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;

use super::LoginKeyFile;

fn login() -> SftpUserName {
    SftpUserName::for_account(&AccountName::parse("alice").unwrap(), "web").unwrap()
}

#[test]
fn the_active_name_is_the_login_in_the_key_directory() {
    let file = LoginKeyFile::for_login(&login());

    assert_eq!(
        file.active().to_str().unwrap(),
        "/etc/ssh/maran-sftp-keys/alice_web"
    );
}

#[test]
fn the_suspended_name_is_the_active_one_with_a_suffix_sshd_never_reads() {
    let file = LoginKeyFile::for_login(&login());

    assert_eq!(
        file.suspended().to_str().unwrap(),
        "/etc/ssh/maran-sftp-keys/alice_web.suspended"
    );
}

#[test]
fn both_names_of_a_logins_file_read_back_as_that_login() {
    // The inverse control of `for_login`: whatever it writes, this reads.
    let file = LoginKeyFile::for_login(&login());
    let active = file.active().file_name().unwrap().to_str().unwrap();
    let suspended = file.suspended().file_name().unwrap().to_str().unwrap();

    assert_eq!(LoginKeyFile::login_named_by(active), Some("alice_web"));
    assert_eq!(LoginKeyFile::login_named_by(suspended), Some("alice_web"));
}

#[test]
fn a_name_that_is_not_the_shape_of_a_logins_is_not_read_as_one() {
    for foreign in [
        "README",
        ".tmpAb12Cd",
        "alice_web.bak",
        "alice_web.suspended.suspended",
        "Alice_web",
        "alice-web",
        "alice_web~",
        ".suspended",
        "",
    ] {
        assert_eq!(
            LoginKeyFile::login_named_by(foreign),
            None,
            "{foreign:?} must be left alone"
        );
    }
}
