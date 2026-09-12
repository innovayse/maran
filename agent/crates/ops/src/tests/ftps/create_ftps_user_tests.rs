//! What an FTPS login is created as, and what a second creation must not do.

// A failing assertion IS the reporting mechanism for a test.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;

use crate::ftps::create_ftps_user::create_ftps_user_under_lock;
use crate::ftps::fake_ftps_host::{FakeFtpsHost, distro, request};
use crate::ftps::ftps_error::FtpsError;
use crate::sftp::AccountOwnership;

/// The account every test here creates a login for.
const ACCOUNT: &str = "alice";

/// The password every test here sets.
///
/// It uses characters from three of the classes `Password` allows, so a pipe
/// that ate punctuation would show as a changed line rather than as nothing.
const PASSWORD: &str = "Gen3rated-pw";

#[test]
fn an_ftps_login_is_created_on_the_accounts_uid_with_no_shell_and_only_the_ftps_group() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    let created = host.created_user().expect("a login was created");
    assert_eq!(created.name, "alice_files");
    assert_eq!(created.uid, 1001);
    assert_eq!(created.gid, 1001);
    assert_eq!(created.shell, distro().nologin_shell());
    assert!(
        created.shell.ends_with("nologin"),
        "the login must have no interactive shell: {}",
        created.shell
    );
    assert_eq!(
        created.groups,
        vec![distro().ftps_group().to_owned()],
        "an FTPS login must not join the SFTP group: the group membership IS the \
         authorization the PAM stack checks, so a login in both opens both daemons"
    );
    assert_eq!(created.home, host.jail().directory());
}

#[test]
fn the_password_is_set_over_stdin_and_never_appears_in_an_argument_vector() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    let spawn = host.last_spawn().expect("a process set the password");
    assert_eq!(spawn.argv, vec![distro().chpasswd_binary().to_owned()]);
    assert_eq!(
        spawn.stdin.as_deref(),
        Some("alice_files:Gen3rated-pw\n"),
        "chpasswd must be given exactly one user:password line"
    );
    assert!(
        !host.spawns().iter().any(|spawn| spawn
            .argv
            .iter()
            .any(|argument| argument.contains(PASSWORD))),
        "no spawn may carry the password in its argument vector: a command line is \
         readable through /proc by every local user on the host"
    );
}

#[test]
fn the_jail_is_root_owned_and_not_writable_by_the_login_that_is_chrooted_into_it() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    let (path, mode) = host
        .created_directory(host.jail().directory())
        .expect("the jail was created");
    assert_eq!(path, format!("/var/lib/maran-ftps/{ACCOUNT}"));
    assert_eq!(
        mode, 0o755,
        "vsftpd refuses to serve a login whose chroot root that login can write"
    );

    let (_, mount_point_mode) = host
        .created_directory(host.jail().mount_point())
        .expect("the mount point was created");
    assert_eq!(
        mount_point_mode, 0o755,
        "the mount point carries the same mode, which is only ever visible while \
         the mount is DOWN: an empty root-owned home is the signature of a failed \
         mount rather than a mystery"
    );
}

#[test]
fn a_jail_base_that_was_not_there_is_created_traversable_but_not_listable() {
    // F1. `create_directory` applies its mode to the LEAF only, so a jail
    // created under an absent base left the base at `create_dir_all`'s 0755 —
    // traversable AND listable, which is exactly what 0711 exists to deny: no
    // account may list the base and learn the names of its neighbours' jails.
    // The fake starts with no directories at all, which is the host this is
    // about.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    let (path, mode) = host
        .created_directory("/var/lib/maran-ftps")
        .expect("the jail base must be created when it is absent");
    assert_eq!(path, "/var/lib/maran-ftps");
    assert_eq!(
        mode, 0o711,
        "traversable by everyone — vsftpd chdir()s into the home AFTER dropping \
         to the account's uid — and listable by nobody but root"
    );

    let created: Vec<String> = host
        .created_directories()
        .into_iter()
        .map(|(path, _)| path)
        .collect();
    let base = created
        .iter()
        .position(|path| path == "/var/lib/maran-ftps")
        .expect("the base is in the list");
    let jail = created
        .iter()
        .position(|path| path == host.jail().directory())
        .expect("the jail is in the list");
    assert!(
        base < jail,
        "the base is created at its own mode BEFORE the jail brings it into \
         existence at the umask default: {created:?}"
    );
}

#[test]
fn the_login_is_created_with_no_home_of_its_own_so_useradd_cannot_take_the_chroot() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    let useradd = host
        .spawns()
        .into_iter()
        .find(|spawn| {
            spawn
                .argv
                .first()
                .is_some_and(|program| program == distro().useradd_binary())
        })
        .expect("useradd ran");
    assert!(
        useradd
            .argv
            .iter()
            .any(|argument| argument == "--no-create-home"),
        "without it useradd creates the home it is given AND chowns it to the new \
         user, which hands the chroot itself to the customer: {:?}",
        useradd.argv
    );
    assert!(
        useradd
            .argv
            .iter()
            .any(|argument| argument == "--non-unique"),
        "the account already holds this uid, and useradd refuses a duplicate \
         unless the duplication is declared: {:?}",
        useradd.argv
    );
}

#[test]
fn creating_a_second_login_for_the_same_account_does_not_write_the_mount_unit_again() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", "pw-one"))
        .expect("the first creation must succeed");
    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "media", "pw-two"))
        .expect("the second creation must succeed");

    assert_eq!(
        host.written_unit_paths(),
        vec![host.jail().unit_path().to_owned()],
        "the jail is ensured on every creation and its unit is written once: a \
         second write would restart a live mount under the first login"
    );
    assert_eq!(host.login_names().len(), 2);
}

#[test]
fn creating_a_login_that_already_exists_reports_already_exists_and_does_not_reset_its_password() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).with_existing_login("alice_files");

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", "new-pw"));

    assert!(
        matches!(result, Err(FtpsError::AlreadyExists)),
        "a retry whose response was lost must converge, not fail: {result:?}"
    );
    assert!(
        !host.spawns().iter().any(|spawn| spawn
            .argv
            .first()
            .is_some_and(|program| program == distro().chpasswd_binary())),
        "the password of a login that already exists must NOT be reset: the \
         customer has already been shown the first one"
    );
}

#[test]
fn a_hosting_account_that_is_not_on_this_host_gets_no_jail_and_no_login() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).answering_ownerships(&[None]);

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD));

    assert!(
        matches!(result, Err(FtpsError::AccountMissing)),
        "{result:?}"
    );
    assert!(
        !host.directory_still_exists(host.jail().directory()),
        "a jail for an account that does not exist is a root-owned directory \
         nothing will ever mount into"
    );
    assert!(host.created_user().is_none());
}

#[test]
fn a_hosting_account_whose_ids_move_before_useradd_gets_no_login() {
    // The identity is read, the jail is built, and the identity is read AGAIN.
    // Between the two the account was deleted and re-created, so the uid now
    // belongs to somebody else — and `useradd --non-unique --uid` would take
    // the number without objecting.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).answering_ownerships(&[
        Some(AccountOwnership {
            uid: 1001,
            gid: 1001,
        }),
        Some(AccountOwnership {
            uid: 1007,
            gid: 1007,
        }),
    ]);

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD));

    assert!(
        matches!(result, Err(FtpsError::AccountIdentityChanged)),
        "{result:?}"
    );
    assert!(
        host.created_user().is_none(),
        "a login carrying a uid that now belongs to the next tenant is a \
         credential into that tenant's files"
    );
}

#[test]
fn a_hosting_account_that_vanishes_before_useradd_gets_no_login() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).answering_ownerships(&[
        Some(AccountOwnership {
            uid: 1001,
            gid: 1001,
        }),
        None,
    ]);

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD));

    assert!(
        matches!(result, Err(FtpsError::AccountMissing)),
        "{result:?}"
    );
    assert!(host.created_user().is_none());
}

#[test]
fn the_hosting_accounts_identity_is_read_again_immediately_before_useradd() {
    // The positive control for the two tests above: they would both also pass
    // against an operation that asked once and cached, if the fake's queue were
    // never drained. This asserts the second question was really put.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    assert_eq!(
        host.ownership_questions(),
        2,
        "a uid resolved before the jail work belongs to whoever useradd has since \
         been given it"
    );
}

#[test]
fn a_jail_that_cannot_be_built_leaves_no_login_behind() {
    // The jail-first ordering, asserted as behaviour rather than as a comment:
    // the incomplete state this operation can leave is a root-owned directory,
    // never a live credential.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).unwritable_config();

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD));

    assert!(matches!(result, Err(FtpsError::JailFailed)), "{result:?}");
    assert!(
        host.created_user().is_none(),
        "a login created before its jail is a working credential whose chroot is \
         an empty directory; a jail with no login is litter"
    );
}

#[test]
fn a_refused_password_is_reported_as_a_refused_password_and_not_as_a_refused_useradd() {
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001).refusing_passwords();

    let result = create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD));

    assert!(
        matches!(result, Err(FtpsError::PasswordRejected)),
        "the login exists and its password is unchanged, which is a different \
         thing from there being no login at all: {result:?}"
    );
}

#[test]
fn the_login_name_that_reaches_useradd_is_the_one_the_account_prefix_built() {
    // The name is never a caller's string: it is rebuilt from the account the
    // panel authorised, so a request cannot address another tenant's login.
    let host = FakeFtpsHost::for_account(ACCOUNT, 1001, 1001);
    let expected = FtpsUserName::for_account(&host.account_name(), "files")
        .expect("a valid login name")
        .as_str()
        .to_owned();

    create_ftps_user_under_lock(&host, distro(), &request(ACCOUNT, "files", PASSWORD))
        .expect("the creation must succeed");

    assert_eq!(host.login_names(), vec![expected]);
}
