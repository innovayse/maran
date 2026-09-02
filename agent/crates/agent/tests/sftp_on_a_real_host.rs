//! SFTP logins against the real OpenSSH daemon, which is the only place
//! `ops::sftp` means anything.
//!
//! Everything this area does is a claim about what a daemon and a kernel will
//! do with what the agent wrote, and a fake can confirm none of it. Four claims
//! are settled here and nowhere else:
//!
//! - **`useradd --no-create-home` against a jail that already exists.** The
//!   flag exists so `useradd` does not create the passwd home AND chown it to
//!   the new login — which would hand the customer the chroot itself, and
//!   OpenSSH would then refuse every login into it. A fake records the flag; a
//!   host obeys it, and the jail's ownership afterwards is the proof.
//! - **`systemctl enable --now` on a freshly written `.mount` unit.** A
//!   `.mount` unit's file name must be systemd's escaping of its own `Where=`
//!   or it will not load, and a mistake shows up on a host as a login that
//!   lands in an empty directory. The name is checked here against
//!   `systemd-escape` — systemd's own tool, so the expectation does not come
//!   from the code under test — and the mount is then really made.
//! - **The chroot.** Asserted as a REFUSAL in a real session, never as a
//!   `ChrootDirectory` line being present in a file: a directive in the wrong
//!   block reads the same and does nothing.
//! - **The credential.** The login authenticates with the password the agent
//!   handed `chpasswd`, which is the only way to find out that it arrived
//!   intact.
//!
//! The daemon reads the `/etc/ssh/sshd_config` the INSTALLER's own `86-sftp.sh`
//! wrote when the image was built. Nothing here writes a line of ssh
//! configuration.
//!
//! These tests need `docker run --privileged`: the bind mount is a real mount.
//! Without it the mount fails, `create_sftp_user` returns `JailFailed`, and
//! these tests fail loudly — they never pass on a jail that was never filled.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_sshd.rs"]
mod polygon_sshd;

use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::sftp::{
    AccountJail, ProcessSftpHost, SftpError, SftpUserRequest, create_sftp_user, delete_sftp_user,
    set_sftp_password,
};

use polygon_account::PolygonAccount;
use polygon_sshd::PolygonSshd;

/// The password every login in this suite is created with.
///
/// It uses every character class `Password` allows — letters, digits and
/// `-_.=+`. A password made only of letters would not notice `chpasswd`, PAM or
/// a pipe eating the punctuation, and the failure would be a customer who
/// cannot log in with what the panel showed them.
const CUSTOMER_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The password a REPEATED creation asks for, and which must not take effect.
///
/// It is also the value a deliberate RESET sets, so one test can show the same
/// password being refused when a repeat offered it and accepted once a reset
/// was asked for — which is the difference between converging and clobbering.
const SECOND_PASSWORD: &str = "Different-2.password";

/// The file each account is given in its home, so a session has something to
/// find and a neighbour has something worth stealing.
const CUSTOMER_FILE: &str = "hello.txt";

/// What that file holds.
const CUSTOMER_CONTENT: &str = "customer data";

/// The distribution adapter for the polygon this suite is running in.
///
/// # Panics
///
/// Panics when the host is outside the support matrix, which a polygon image
/// never is.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The jail paths for `account`, derived exactly as the operation derives them.
fn jail_of(account: &AccountName) -> AccountJail {
    AccountJail::for_account(account, polygon_distro().systemd_unit_directory())
}

/// One real SFTP login, created for a test and revoked when it ends.
///
/// The login is made by the code under test. What this type adds is the
/// teardown: the login is removed and the account's bind mount taken down, so
/// one test's mount cannot be what a later test is really looking at.
struct PolygonSftpLogin {
    /// The login's validated name.
    user: SftpUserName,
    /// The account's jail, kept so the teardown unmounts the right path.
    jail: AccountJail,
}

impl PolygonSftpLogin {
    /// Creates `account`'s `web` login through `create_sftp_user`.
    ///
    /// # Panics
    ///
    /// Panics when the operation refuses — including when the bind mount could
    /// not be made, which is what a run without `--privileged` looks like.
    fn create(account: &PolygonAccount) -> Self {
        let user = SftpUserName::for_account(account.name(), "web").expect("a valid login name");
        let request = SftpUserRequest {
            account: account.name().clone(),
            user: user.clone(),
            password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
        };

        create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &request).unwrap_or_else(
            |error| {
                panic!(
                    "creating an SFTP login must succeed in the polygon: {error}. \
                     A JailFailed here usually means the container was started \
                     without --privileged, so the bind mount could not be made."
                )
            },
        );

        Self {
            user,
            jail: jail_of(account.name()),
        }
    }

    /// The login's full name, as the host holds it.
    fn name(&self) -> &str {
        self.user.as_str()
    }
}

impl Drop for PolygonSftpLogin {
    /// Revokes the login and takes the account's bind mount down.
    fn drop(&mut self) {
        // A failure here cannot fail the test — a panic in `drop` during another
        // panic aborts the process and hides the real failure — so it is
        // reported and nothing more.
        if let Err(error) = delete_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &self.user)
        {
            eprintln!(
                "the polygon login {} could not be removed: {error}",
                self.name()
            );
        }

        let unmounted = Command::new("umount").arg(self.jail.mount_point()).output();
        if let Ok(outcome) = unmounted
            && !outcome.status.success()
        {
            eprintln!(
                "the polygon jail {} could not be unmounted: {}",
                self.jail.mount_point(),
                String::from_utf8_lossy(&outcome.stderr)
            );
        }
    }
}

/// Puts a file in `account`'s home, owned by the account, and returns its path.
fn plant_file(account: &PolygonAccount) -> PathBuf {
    let path = account.home().join(CUSTOMER_FILE);
    std::fs::write(&path, CUSTOMER_CONTENT).expect("the account's home must be writable by root");
    std::os::unix::fs::chown(&path, Some(account.ids().uid()), Some(account.ids().gid()))
        .expect("the planted file must belong to the account");

    path
}

/// Everything the sftp client printed, both streams together.
fn said(output: &std::process::Output) -> String {
    format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    )
}

#[test]
#[ignore = "compares against systemd's own escaping: polygon only"]
fn the_mount_units_name_is_the_one_systemd_derives_from_its_own_mount_point() {
    PolygonSshd::require_polygon();

    // The rule this pins: systemd refuses to load a `.mount` unit whose file
    // name is not the escaping of its own `Where=`. A friendlier name would be
    // rejected at LOAD time, on a host, appearing as an SFTP login that lands in
    // an empty directory — never in a build.
    //
    // The expectation comes from `systemd-escape`, which is systemd's own
    // implementation of the rule. Deriving it any other way would let a bug in
    // the agent's escaping agree with a bug in the test.
    let account = AccountName::parse("polysftpescape").expect("a valid account name");
    let jail = jail_of(&account);

    let escaped = Command::new("systemd-escape")
        .args(["--path", "--suffix=mount", jail.mount_point()])
        .output()
        .unwrap_or_else(|error| panic!("the polygon image installs systemd-escape: {error}"));
    assert!(escaped.status.success(), "systemd-escape must answer");

    assert_eq!(
        jail.unit_name(),
        String::from_utf8_lossy(&escaped.stdout).trim(),
        "the unit's file name must be systemd's own escaping of Where={}",
        jail.mount_point()
    );
}

#[test]
#[ignore = "creates a real system account and mounts a real filesystem: polygon only"]
fn creating_a_login_builds_a_jail_useradd_did_not_touch_and_mounts_the_home_into_it() {
    PolygonSshd::require_polygon();
    let account = PolygonAccount::create("polysftpone");
    let planted = plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let jail = jail_of(account.name());

    // `--no-create-home` obeyed by the real tool. `useradd`'s default for a
    // missing home is to create it AND chown it to the new login; against a
    // passwd home that IS the chroot, that hands the customer the directory
    // OpenSSH is about to chroot into — which OpenSSH then refuses, and which
    // is where every chroot escape starts. A fake can record the flag; only a
    // host can be asked what the flag did.
    let owner = std::fs::metadata(jail.directory()).expect("the jail must exist");
    assert_eq!(owner.uid(), 0, "the chroot must still be owned by root");
    assert_eq!(
        owner.gid(),
        0,
        "the chroot must still be group-owned by root"
    );
    assert_eq!(
        owner.mode() & 0o777,
        0o755,
        "OpenSSH refuses to chroot into a group- or world-writable directory"
    );

    // The unit is on disk under the one name systemd would accept, and the mount
    // it describes has really happened: the account's file is visible inside the
    // jail. Nothing else in the project can tell a working bind mount from a
    // jail with an empty `home` directory in it.
    assert!(
        Path::new(jail.unit_path()).exists(),
        "the bind-mount unit must be installed at {}",
        jail.unit_path()
    );
    let inside = Path::new(jail.mount_point()).join(CUSTOMER_FILE);
    assert_eq!(
        std::fs::read_to_string(&inside).unwrap_or_default(),
        CUSTOMER_CONTENT,
        "the account's real home must appear inside its jail at {}",
        inside.display()
    );

    // And the account's own home is untouched — the whole reason the jail
    // exists rather than the home being chrooted into directly.
    let home = std::fs::metadata(account.home()).expect("the home must exist");
    assert_eq!(
        home.uid(),
        account.ids().uid(),
        "the account's home must still belong to the account"
    );
    assert_eq!(
        home.mode() & 0o777,
        0o750,
        "the account's home must keep the mode every site and pool depends on"
    );
    assert!(planted.exists(), "the planted file must still be there");

    drop(login);
}

#[test]
#[ignore = "creates a real system account and drops it: polygon only"]
fn an_sftp_user_logs_in_and_is_jailed_in_its_own_home_and_cannot_reach_another_accounts() {
    let sshd = PolygonSshd::start();
    let mine = PolygonAccount::create("polysftptwo");
    let neighbour = PolygonAccount::create("polysftpthree");
    plant_file(&mine);
    let stolen_from = plant_file(&neighbour);
    let login = PolygonSftpLogin::create(&mine);

    // 1. The login works, with the password the agent set, and lands in the
    //    jail rather than anywhere on the host's filesystem. `/` in the session
    //    IS the chroot: what it lists is what a customer can see at all.
    let session = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\nls\ncd home\nls\n");
    assert!(
        session.status.success(),
        "the login must work with the password the agent set:\n{}",
        said(&session)
    );
    let transcript = said(&session);
    assert!(
        transcript.contains("Remote working directory: /"),
        "the session must start at the root of its chroot:\n{transcript}"
    );
    assert!(
        transcript.contains(CUSTOMER_FILE),
        "the account's own file must be visible through the bind mount:\n{transcript}"
    );

    // 1b. And a password that is NOT the one the agent set is refused. Without
    //     this, every assertion in this file would also hold on a daemon that
    //     let anybody in, and "the credential works" would mean nothing.
    let wrong = sshd.sftp(login.name(), "Wr0ng-password", "pwd\n");
    assert!(
        !wrong.status.success(),
        "the daemon must refuse a password that is not the one that was set:\n{}",
        said(&wrong)
    );

    // 2. The chroot, asserted as a refusal in a real session. `/etc` exists on
    //    this host and is world-readable; inside the chroot there is no such
    //    path at all, which is what "chrooted" means and what a `ChrootDirectory`
    //    line in the wrong block would not achieve.
    let escape = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "cd /etc\n");
    assert!(
        !escape.status.success(),
        "an SFTP login must not be able to leave its jail:\n{}",
        said(&escape)
    );
    let refusal = said(&escape);
    assert!(
        refusal.contains("No such file") || refusal.contains("not found"),
        "the refusal must be the path not existing inside the chroot, not some \
         other failure:\n{refusal}"
    );

    // 3. The neighbour's file, by absolute path. It is readable to root on this
    //    host and belongs to a different account; from inside the chroot the
    //    path does not resolve, and nothing is fetched.
    let neighbour_path = format!("/home/{}/{CUSTOMER_FILE}", neighbour.name().as_str());
    let landing = std::env::temp_dir().join("maran-polygon-stolen.txt");
    let _ = std::fs::remove_file(&landing);
    let theft = sshd.sftp(
        login.name(),
        CUSTOMER_PASSWORD,
        &format!("get {neighbour_path} {}\n", landing.display()),
    );
    assert!(
        !theft.status.success(),
        "an SFTP login must not reach another account's files:\n{}",
        said(&theft)
    );
    assert!(
        !landing.exists(),
        "nothing may have been fetched from another account"
    );
    // The neighbour's file is genuinely there and genuinely worth having, so
    // the refusal above is a refusal and not a test that asked for nothing.
    assert_eq!(
        std::fs::read_to_string(&stolen_from).unwrap_or_default(),
        CUSTOMER_CONTENT
    );

    drop(login);
}

#[test]
#[ignore = "creates a real system account and drops it: polygon only"]
fn an_sftp_user_can_write_through_the_bind_mount_and_the_file_belongs_to_the_account() {
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpfour");
    let login = PolygonSftpLogin::create(&account);

    let source = std::env::temp_dir().join("maran-polygon-upload.txt");
    std::fs::write(&source, CUSTOMER_CONTENT).expect("a local file to upload");

    let session = sshd.sftp(
        login.name(),
        CUSTOMER_PASSWORD,
        &format!("cd home\nput {} uploaded.txt\n", source.display()),
    );
    assert!(
        session.status.success(),
        "a customer must be able to upload into its own home:\n{}",
        said(&session)
    );

    // The file arrived in the REAL home, through the bind mount, and it belongs
    // to the account rather than to some identity of the login's own. That last
    // part is what makes the login usable at all: a file owned by anything else
    // is a file the account's own php-fpm pool cannot open.
    let uploaded = account.home().join("uploaded.txt");
    assert_eq!(
        std::fs::read_to_string(&uploaded).unwrap_or_default(),
        CUSTOMER_CONTENT,
        "the upload must land in the account's real home at {}",
        uploaded.display()
    );
    let owner = std::fs::metadata(&uploaded).expect("the uploaded file must exist");
    assert_eq!(
        owner.uid(),
        account.ids().uid(),
        "an uploaded file must belong to the account, exactly as one the \
         account created itself"
    );

    drop(login);
}

#[test]
#[ignore = "creates a real system account: polygon only"]
fn an_sftp_user_gets_no_shell_even_over_ssh_exec() {
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpfive");
    let login = PolygonSftpLogin::create(&account);

    // `ForceCommand internal-sftp` plus a nologin shell. The assertion is on
    // what happened, not on a directive being present somewhere in a file: a
    // `ForceCommand` outside the Match block reads identically and would leave
    // this login with a shell on the host.
    let attempt = sshd.exec(login.name(), CUSTOMER_PASSWORD, "whoami");
    assert!(
        !attempt.status.success(),
        "an SFTP login must not be able to execute a command:\n{}",
        said(&attempt)
    );
    let printed = String::from_utf8_lossy(&attempt.stdout);
    assert!(
        !printed.contains(login.name()),
        "`whoami` must not have RUN — it printed the login name:\n{printed}"
    );

    // The assertion above is satisfied by a login that failed to authenticate,
    // which would make this test pass while proving nothing at all — the exact
    // shape of vacuous test rules/testing.md warns about. So the refusal is
    // required to be the SESSION being refused rather than the credential: the
    // login got in, and then was told this service moves files and nothing else.
    let refusal = said(&attempt);
    assert!(
        !refusal.contains("Permission denied"),
        "the login must have AUTHENTICATED and then been refused a command; a \
         failed login would satisfy every other assertion here:\n{refusal}"
    );
    assert!(
        refusal.contains("sftp"),
        "the refusal must name the forced subsystem, so it is the ForceCommand \
         that refused and not something else:\n{refusal}"
    );

    drop(login);
}

#[test]
#[ignore = "asks the real password database about an account: polygon only"]
fn a_login_for_an_account_this_host_does_not_have_is_refused_and_builds_nothing() {
    PolygonSshd::require_polygon();

    // No `PolygonAccount` here on purpose: the account genuinely does not exist,
    // so this is the real `getpwnam` refusing rather than a fake saying no.
    let account = AccountName::parse("polysftpnobody").expect("a valid account name");
    let user = SftpUserName::for_account(&account, "web").expect("a valid login name");
    let request = SftpUserRequest {
        account: account.clone(),
        user,
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };

    let refused = create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &request);
    assert!(
        matches!(refused, Err(SftpError::AccountMissing)),
        "a login for an account that does not exist must be refused, got {refused:?}"
    );

    // And nothing was built on the way to that refusal. A jail left behind for a
    // non-existent account is a root-owned directory nothing will ever mount
    // into, and a login created against it could read nothing at all.
    let jail = jail_of(&account);
    assert!(
        !Path::new(jail.directory()).exists(),
        "no jail may be left behind at {}",
        jail.directory()
    );
    assert!(
        !Path::new(jail.unit_path()).exists(),
        "no mount unit may be left behind at {}",
        jail.unit_path()
    );
}

#[test]
#[ignore = "creates, re-credits and removes a real system account: polygon only"]
fn repeating_every_sftp_operation_converges_and_only_a_reset_changes_the_credential() {
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpfive");
    let login = PolygonSftpLogin::create(&account);
    let user = SftpUserName::for_account(account.name(), "web").expect("a valid login name");

    // 1. A repeated creation converges on AlreadyExists rather than failing.
    //    The caller cannot tell a lost response from a lost request, so it
    //    retries; a retry that failed would leave the panel unable to finish an
    //    operation the host had already completed.
    let repeated = create_sftp_user(
        &ProcessSftpHost::new(),
        polygon_distro(),
        &SftpUserRequest {
            account: account.name().clone(),
            user: user.clone(),
            password: Password::parse(SECOND_PASSWORD).expect("a valid password"),
        },
    );
    assert!(
        matches!(repeated, Err(SftpError::AlreadyExists)),
        "a repeated creation must converge, got {repeated:?}"
    );

    // And the repeat's password never took effect. Asserted against the daemon,
    // because that is the only thing that knows which credential is live: a
    // retry that reset the password would silently invalidate the value the
    // customer was shown once and cannot recover.
    let original = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        original.status.success(),
        "the first password must still work after a repeated create:\n{}",
        said(&original)
    );
    let unused = sshd.sftp(login.name(), SECOND_PASSWORD, "pwd\n");
    assert!(
        !unused.status.success(),
        "the repeat's password must never have been set:\n{}",
        said(&unused)
    );

    // 2. A reset DOES change it, and the old value stops working. This is the
    //    one operation here whose whole purpose is to replace a credential, and
    //    a reset that merely added one would look identical from the panel.
    set_sftp_password(
        &ProcessSftpHost::new(),
        polygon_distro(),
        &user,
        &Password::parse(SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("resetting the password must succeed: {error}"));

    let renewed = sshd.sftp(login.name(), SECOND_PASSWORD, "pwd\n");
    assert!(
        renewed.status.success(),
        "the login must work with the reset password:\n{}",
        said(&renewed)
    );
    let stale = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !stale.status.success(),
        "the replaced password must no longer authenticate:\n{}",
        said(&stale)
    );

    // 3. Repeating the reset succeeds and leaves the same value working.
    set_sftp_password(
        &ProcessSftpHost::new(),
        polygon_distro(),
        &user,
        &Password::parse(SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("a repeated reset must succeed: {error}"));
    let after = sshd.sftp(login.name(), SECOND_PASSWORD, "pwd\n");
    assert!(
        after.status.success(),
        "a repeated reset must leave the same password working:\n{}",
        said(&after)
    );

    // 4. The deletion, then the deletion again. The fixture's teardown performs
    //    the first one and takes the account's mount down with it, so the second
    //    is asked of a host that really no longer has the login.
    drop(login);

    let again = delete_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &user);
    assert!(
        matches!(again, Err(SftpError::NotFound)),
        "a second deletion must converge on NotFound, got {again:?}"
    );

    // And the login is gone from the real password database, not merely reported
    // as absent by the tool's exit status.
    let remaining = Command::new("getent")
        .args(["passwd", user.as_str()])
        .output()
        .expect("getent must run in the polygon");
    assert!(
        !remaining.status.success(),
        "the deleted login must be gone from the password database: {}",
        String::from_utf8_lossy(&remaining.stdout)
    );
}
