//! What `create_sftp_user` builds, in which order, and what it refuses to do
//! twice.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::password::Password;

use crate::sftp::create_sftp_user::create_sftp_user;
use crate::sftp::fake_sftp_host::{
    ACCOUNT_GID, ACCOUNT_UID, FakeSftpHost, TEST_PASSWORD, distro, web_request,
};
use crate::sftp::sftp_error::SftpError;

/// The argument `useradd` was given after `flag`, if it was given one.
fn argument_after(argv: &[String], flag: &str) -> Option<String> {
    argv.iter()
        .position(|argument| argument == flag)
        .and_then(|at| argv.get(at + 1))
        .cloned()
}

/// The login joins the chroot group and gets no shell.
#[test]
fn creating_an_sftp_user_puts_it_in_the_chroot_group_with_a_nologin_shell() {
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let useradd = host.spawn_of("useradd").expect("useradd was run");
    assert_eq!(useradd.argv.last().expect("a login"), "alice_web");
    assert!(
        useradd.argv.iter().any(|argument| argument == "maran-sftp"),
        "the login must be in the group sshd's Match block chroots: {:?}",
        useradd.argv
    );
    let shell = useradd
        .argv
        .iter()
        .position(|argument| argument == "--shell")
        .and_then(|at| useradd.argv.get(at + 1))
        .expect("a shell argument");
    assert!(
        shell.ends_with("nologin"),
        "an SFTP user must not get a shell: {shell}"
    );
}

/// The passwd home is the jail, and `useradd` is told not to touch it.
#[test]
fn creating_an_sftp_user_points_its_home_at_the_jail_and_forbids_useradd_from_creating_it() {
    // `useradd` creates a missing home AND chowns it to the new user. Doing
    // that to the jail would hand the chroot itself to the customer, which
    // OpenSSH refuses to chroot into and which is where a chroot escape starts.
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let useradd = host.spawn_of("useradd").expect("useradd was run");
    let home = useradd
        .argv
        .iter()
        .position(|argument| argument == "--home-dir")
        .and_then(|at| useradd.argv.get(at + 1))
        .expect("a home argument");
    assert_eq!(home, "/var/lib/maran/sftp/alice");
    assert!(
        useradd
            .argv
            .iter()
            .any(|argument| argument == "--no-create-home"),
        "useradd must not create or chown the jail: {:?}",
        useradd.argv
    );
    assert!(
        !useradd
            .argv
            .iter()
            .any(|argument| argument == "/home/alice"),
        "the account's own home is never passed to useradd: {:?}",
        useradd.argv
    );
}

/// The login is created with the account's own uid and gid, not its own.
#[test]
fn creating_an_sftp_user_gives_it_the_accounts_own_user_and_group_ids() {
    // A home of `<account>:<web server group> 0750` gives an identity of its own
    // nothing at all, so a login with one lands in its jail and cannot read a
    // single file of the account it was made for. Sharing the ids is also what
    // makes an uploaded file come out owned by the account.
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let useradd = host.spawn_of("useradd").expect("useradd was run");
    assert_eq!(
        argument_after(&useradd.argv, "--uid"),
        Some(ACCOUNT_UID.to_string()),
        "the login must carry the account's uid: {:?}",
        useradd.argv
    );
    assert_eq!(
        argument_after(&useradd.argv, "--gid"),
        Some(ACCOUNT_GID.to_string()),
        "the login must carry the account's gid: {:?}",
        useradd.argv
    );
    assert!(
        useradd
            .argv
            .iter()
            .any(|argument| argument == "--non-unique"),
        "the account already holds that uid, so useradd must be told the \
         duplication is deliberate: {:?}",
        useradd.argv
    );
}

/// The login never joins the web server's group, which spans every tenant.
#[test]
fn creating_an_sftp_user_does_not_put_it_in_the_web_servers_group() {
    // That group traverses EVERY account's home by design. A login in it would
    // be one customer's credential with read access to every other customer's
    // files — the exact failure the jail exists to prevent.
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let useradd = host.spawn_of("useradd").expect("useradd was run");
    let web_group = distro().web_server_group();
    assert!(
        !useradd
            .argv
            .iter()
            .any(|argument| argument.split(',').any(|group| group == web_group)),
        "an SFTP login must never be in {web_group}: {:?}",
        useradd.argv
    );
}

/// A login for an account this host does not have is never created.
#[test]
fn creating_an_sftp_user_for_an_unknown_account_is_refused_before_anything_is_made() {
    let host = FakeSftpHost::new();
    host.forget_the_account();

    let error = create_sftp_user(&host, distro(), &web_request()).expect_err("must fail");

    assert!(matches!(error, SftpError::AccountMissing));
    assert!(
        host.users().is_empty() && host.directories().is_empty() && host.configs().is_empty(),
        "nothing may be built for an account that does not exist: {:?}",
        host.spawns()
    );
}

/// The jail's directories are made root-safe before the login exists.
#[test]
fn creating_an_sftp_user_makes_the_jail_and_its_mount_point_before_the_login() {
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let directories = host.directories();
    assert_eq!(
        directories,
        vec![
            ("/var/lib/maran/sftp/alice".to_owned(), 0o755),
            ("/var/lib/maran/sftp/alice/home".to_owned(), 0o755),
        ],
        "the jail must be created, and at a mode OpenSSH will chroot into"
    );
    assert!(
        !host.configs().is_empty(),
        "the bind mount must be installed as a unit"
    );
}

/// The mount is a unit file, named as systemd names one, and it mounts the
/// real home into the jail.
#[test]
fn creating_an_sftp_user_installs_an_enabled_bind_mount_unit_for_the_account() {
    // A `mount` call would be gone at the next boot and every login for the
    // account would land in an empty jail. An enabled unit is re-established on
    // every boot instead.
    let host = FakeSftpHost::new();

    create_sftp_user(&host, distro(), &web_request()).expect("created");

    let configs = host.configs();
    let unit = configs.first().expect("a unit was written");
    assert_eq!(
        unit.target,
        "/etc/systemd/system/var-lib-maran-sftp-alice-home.mount"
    );
    assert!(unit.contents.contains("What=/home/alice"));
    assert!(
        unit.contents
            .contains("Where=/var/lib/maran/sftp/alice/home")
    );
    assert!(unit.contents.contains("Options=bind"));
    assert!(
        unit.validator.contains(&"daemon-reload".to_owned()),
        "the unit must be parsed before it is relied on: {:?}",
        unit.validator
    );
    assert!(
        unit.reload.contains(&"--now".to_owned())
            && unit
                .reload
                .contains(&"var-lib-maran-sftp-alice-home.mount".to_owned()),
        "the unit must be enabled and started: {:?}",
        unit.reload
    );
}

/// A jail that could not be built stops the operation before any login exists.
#[test]
fn a_jail_that_cannot_be_installed_stops_the_creation_before_the_login_is_made() {
    let host = FakeSftpHost::new();
    host.refuse_config_writes();

    let error = create_sftp_user(&host, distro(), &web_request()).expect_err("must fail");

    assert!(matches!(error, SftpError::JailFailed));
    assert!(
        host.users().is_empty(),
        "no login may exist without the jail it logs in to"
    );
}

/// A repeat converges on `AlreadyExists` instead of failing.
#[test]
fn creating_a_user_that_already_exists_reports_already_exists() {
    let host = FakeSftpHost::with_existing("alice_web");

    let error = create_sftp_user(&host, distro(), &web_request()).expect_err("must fail");

    assert!(matches!(error, SftpError::AlreadyExists));
}

/// A repeat leaves the existing login's password alone.
#[test]
fn creating_a_user_that_already_exists_does_not_reset_its_password() {
    // The caller cannot tell a lost response from a lost request, so it retries.
    // A retry that reset the password would invalidate the credential the
    // customer was already shown.
    let host = FakeSftpHost::with_existing("alice_web");

    create_sftp_user(&host, distro(), &web_request()).expect_err("must fail");

    assert!(
        host.spawn_of("chpasswd").is_none(),
        "an existing login's password must not be touched: {:?}",
        host.spawns()
    );
}

/// A password the type forbids cannot break the `chpasswd` line.
#[test]
fn a_password_the_type_forbids_cannot_break_the_chpasswd_line() {
    // A newline in the password would inject a second `user:password` line into
    // chpasswd's stdin — that is, set another account's password. Password
    // forbids the newline, so there is no such value to pass.
    assert!(Password::parse("pw\nroot:owned").is_err());
    assert!(Password::parse("pw:extra").is_err());
}

/// A refusal names a typed variant and carries none of the tool's words.
#[test]
fn a_refusal_carries_a_typed_variant_and_never_the_password() {
    let host = FakeSftpHost::new();
    host.refuse_password_with(1);

    let error = create_sftp_user(&host, distro(), &web_request()).expect_err("must fail");

    assert!(matches!(error, SftpError::PasswordRejected));
    let printed = format!("{error:?} {error}");
    assert!(!printed.contains(TEST_PASSWORD));
}
