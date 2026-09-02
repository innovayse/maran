//! Where the password goes, and where it must never go.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::password::Password;

use crate::sftp::fake_sftp_host::{FakeSftpHost, TEST_PASSWORD, distro, web_request, web_user};
use crate::sftp::set_sftp_password::set_sftp_password;
use crate::sftp::sftp_error::SftpError;

/// The password reaches `chpasswd` on standard input and appears in no
/// argument vector.
#[test]
fn the_password_is_set_over_stdin_and_never_appears_in_an_argument_vector() {
    // The leak this closes: a password on a command line is visible in `ps` to
    // every local user on the host. chpasswd reads it from standard input; the
    // argv array carries only the program.
    let host = FakeSftpHost::new();
    let request = web_request();

    set_sftp_password(&host, distro(), &request.user, &request.password).expect("set");

    let spawn = host
        .last_spawn()
        .expect("a process was spawned to set the password");
    assert_eq!(spawn.argv, vec!["/usr/sbin/chpasswd".to_owned()]);
    assert!(spawn.stdin.contains("alice_web:"));
    assert!(
        !spawn
            .argv
            .iter()
            .any(|argument| argument.contains(TEST_PASSWORD)),
        "the password must not be in the argument vector: {:?}",
        spawn.argv
    );
}

/// Creating a login puts its password on standard input too, not just the
/// standalone operation.
#[test]
fn creating_a_user_also_keeps_the_password_out_of_every_argument_vector() {
    let host = FakeSftpHost::new();

    crate::sftp::create_sftp_user::create_sftp_user(&host, distro(), &web_request())
        .expect("created");

    for spawn in host.spawns() {
        assert!(
            !spawn
                .argv
                .iter()
                .any(|argument| argument.contains(TEST_PASSWORD)),
            "a password reached an argument vector: {:?}",
            spawn.argv
        );
    }
    let chpasswd = host.spawn_of("chpasswd").expect("chpasswd was run");
    assert!(chpasswd.stdin.contains(TEST_PASSWORD));
}

/// Exactly one line reaches the tool, terminated so it is read at all.
#[test]
fn exactly_one_user_and_password_line_reaches_the_tool() {
    // Two lines would be two passwords set, which is the injection the Password
    // alphabet exists to make impossible. One line is what proves the format is
    // what the alphabet was designed against.
    let host = FakeSftpHost::new();
    let request = web_request();

    set_sftp_password(&host, distro(), &request.user, &request.password).expect("set");

    let spawn = host.last_spawn().expect("a spawn");
    assert_eq!(spawn.stdin, format!("alice_web:{TEST_PASSWORD}\n"));
    assert_eq!(spawn.stdin.lines().count(), 1);
}

/// A refused password is its own condition, and the tool's words do not travel.
#[test]
fn a_refused_password_is_reported_as_password_rejected() {
    let host = FakeSftpHost::new();
    host.refuse_password_with(1);
    let password = Password::parse(TEST_PASSWORD).expect("valid");

    let error = set_sftp_password(&host, distro(), &web_user(), &password).expect_err("must fail");

    assert!(matches!(error, SftpError::PasswordRejected));
    let printed = format!("{error:?} {error}");
    assert!(!printed.contains(TEST_PASSWORD));
}

/// The error type has nowhere to put a password, whatever a caller formats.
#[test]
fn a_password_prints_as_a_placeholder_wherever_a_request_is_formatted() {
    let request = web_request();

    assert!(!format!("{request:?}").contains(TEST_PASSWORD));
}
