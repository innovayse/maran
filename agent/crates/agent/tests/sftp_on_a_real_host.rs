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
#[path = "fixtures/sftp_control_session.rs"]
mod sftp_control_session;

use std::io::Write as _;
use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountOperations, ProcessSystemHost, StoredPassword};
use maran_ops::cron::ProcessCronHost;
use maran_ops::logins::{ProcessLoginsHost, account_logins, set_account_logins_locked};
use maran_ops::sftp::{
    AccountJail, ProcessSftpHost, SftpError, SftpUserRequest, create_sftp_user, delete_sftp_user,
    set_sftp_password,
};
use maran_ops::sites::ProcessSiteHost;

use polygon_account::PolygonAccount;
use polygon_sshd::PolygonSshd;
use sftp_control_session::SftpControlSession;

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
    /// The hosting account the login belongs to.
    ///
    /// Kept because the deletion takes it: the operation refuses a login the
    /// account does not hold, and the account is the only thing that can tell
    /// `alice`'s login `bob` from the hosting account `alice_bob`.
    account: AccountName,
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
            account: account.name().clone(),
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
        if let Err(error) = delete_sftp_user(
            &ProcessSftpHost::new(),
            polygon_distro(),
            &self.account,
            &self.user,
        ) {
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
        account.name(),
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
        account.name(),
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

    let again = delete_sftp_user(
        &ProcessSftpHost::new(),
        polygon_distro(),
        account.name(),
        &user,
    );
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

#[test]
#[ignore = "authenticates against a real sshd after a real suspension: polygon only"]
fn suspending_the_account_refuses_the_sftp_login_that_worked_before_it() {
    // P4. The proposition the design names, and the defect it exists to close:
    // an SFTP login is its OWN passwd entry — `<account>_<name>`, created with
    // `useradd --non-unique --uid <account uid>` — so `usermod --lock <account>`
    // prefixes the ACCOUNT's hash and touches none of them. A suspended customer
    // therefore kept a working WRITE credential into their own home.
    //
    // It is asserted as a real refusal in a real session, never as a `!` read
    // out of `/etc/shadow`: a locked hash is not the only thing that decides an
    // authentication, and the only question worth answering is what the daemon
    // does.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpsusp");
    plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let operations =
        AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro());

    // The inverse control, and it is not optional here: an assertion that a
    // login is refused is satisfied by a login that never worked — a wrong
    // password, a jail that was never mounted, an sshd that is not listening.
    let before = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        before.status.success(),
        "the login must work BEFORE the suspension, or the refusal below proves nothing:\n{}",
        said(&before)
    );

    // The two halves of a suspension, in the order the panel drives them: the
    // account's own passwd entry, and then every SFTP login it holds. The
    // second call is the whole of this test's subject — `usermod --lock` on the
    // account reaches none of these entries, because each is its own.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    let after = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !after.status.success(),
        "a suspended account's SFTP login must be refused, and it is a live write \
         credential into the customer's home for as long as it is not:\n{}",
        said(&after)
    );

    // The agent's own answer about the same login, checked against what the
    // daemon just did rather than against the code that wrote it. This is the
    // observation the panel refuses a suspension on.
    let observed = account_logins(&ProcessLoginsHost::new(), polygon_distro(), account.name())
        .expect("the password database must be readable");
    assert_eq!(
        observed.logins.len(),
        1,
        "the account holds exactly its one login"
    );
    assert!(
        observed.logins.iter().all(|login| login.locked),
        "the attestation must report as locked the login sshd just refused: {observed:?}"
    );
    assert_eq!(
        observed.unmanaged, 0,
        "this account holds no login outside its jails, and a count that said otherwise \
         would mean the attestation is measuring against the wrong uid: {observed:?}"
    );

    // And the reversal, driven in FULL: both halves of the resume, in the order
    // the panel drives them, on a real account whose login a real daemon just
    // refused.
    //
    // The account half used to be left out of this test, because
    // `AccountOperations::unsuspend` could not complete on the RHEL family at
    // all: a hosting account is created by `useradd` and is never given a
    // password by this agent, `usermod --unlock` refuses such a login with
    // "unlocking the user's password would result in a passwordless account",
    // and the two families spell that refusal as exit 0 and exit 1
    // respectively. `unsuspend` now asks what the account's shadow field
    // actually holds and runs the unlock only where there is a password behind
    // the lock, so the call belongs here — and it is what makes this
    // proposition a reversal rather than half of one. If that fix regressed,
    // this line is what would see it, on the RHEL image, as a failed resume.
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));

    let resumed = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        resumed.status.success(),
        "resuming must give the login back:\n{}",
        said(&resumed)
    );

    drop(login);
}

#[test]
#[ignore = "changes a real login's password under a real suspension: polygon only"]
fn changing_an_sftp_password_while_suspended_does_not_hand_the_login_back() {
    // P9. M-4, the half that is not a race, on the instrument that can see it.
    //
    // `chpasswd` REPLACES the shadow password field; a suspension is a `!`
    // written in front of that same field. So a suspended customer who changed
    // their own SFTP password through the panel got a fresh unmarked hash and an
    // authenticating login, with the panel still reporting the account as
    // suspended. No race, no attacker, no second actor.
    //
    // **The assertions are on the RAW shadow field**, read by a separate
    // `getent` process. An assertion that the account "is still suspended"
    // according to the panel's own record would pass against this very defect,
    // because the panel's record is exactly what stays wrong. The field is the
    // byte string PAM consults, so an assertion on it is an assertion about
    // whether a password can authenticate — and the daemon is asked as well,
    // because a locked hash is not the only thing that decides an authentication.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftppwsus");
    plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let user = SftpUserName::for_account(account.name(), "web").expect("a valid login name");
    let operations =
        AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro());

    // The inverse control. Everything below is about a credential being taken
    // away and kept away, and every one of those assertions is satisfied by a
    // login that never worked at all.
    let working = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        working.status.success(),
        "the login must work BEFORE the suspension, or nothing below proves anything:\n{}",
        said(&working)
    );
    let credited = shadow_password_field(login.name());
    assert!(
        !credited.starts_with('!') && credited.len() > 1,
        "a working login must hold a usable hash before the suspension: {credited}"
    );

    // The suspension, both halves, in the order the panel drives them.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    let suspended = shadow_password_field(login.name());
    assert!(
        suspended.starts_with('!'),
        "the suspension must really lock the login's field, or the change below has \
         nothing to undo: {suspended}"
    );

    // The defect, driven exactly as a suspended customer drives it: one call to
    // the operation the panel offers them.
    set_sftp_password(
        &ProcessSftpHost::new(),
        polygon_distro(),
        account.name(),
        &user,
        &Password::parse(SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("changing the password must still succeed: {error}"));

    // THE assertion. The lock character is what the defect removes, and this is
    // the value it must hold — not "the field changed", not "the panel still
    // says suspended".
    let after = shadow_password_field(login.name());
    assert!(
        after.starts_with('!'),
        "the lock character is gone from the shadow field after a password change: a \
         suspended account has just unlocked its own login. The field is {after}, and it \
         was {suspended} while suspended"
    );
    // And the password really was set underneath the lock — the fix restores a
    // suspension, it does not refuse the operation. Without this, "never run
    // chpasswd at all" would pass the assertion above.
    assert!(
        after.len() > 1 && after != suspended,
        "the new password must have been written under the lock: the field is still {after}"
    );

    // The daemon's own answer about the same login, for both credentials: the
    // one that was replaced and the one that just replaced it.
    let with_the_old = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !with_the_old.status.success(),
        "the replaced password must not authenticate a suspended login:\n{}",
        said(&with_the_old)
    );
    let with_the_new = sshd.sftp(login.name(), SECOND_PASSWORD, "pwd\n");
    assert!(
        !with_the_new.status.success(),
        "a suspended account's SFTP login must stay refused after its own password \
         change, and it is a live write credential into the customer's home for as long \
         as it is not:\n{}",
        said(&with_the_new)
    );

    // And the resume gives back the password that was set while suspended,
    // which is the promise the fix makes: the customer's new credential was
    // stored, not thrown away.
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));

    let resumed = sshd.sftp(login.name(), SECOND_PASSWORD, "pwd\n");
    assert!(
        resumed.status.success(),
        "the resume must hand back the password the customer set while suspended:\n{}",
        said(&resumed)
    );

    drop(login);
}

#[test]
#[ignore = "locks and unlocks a real account's own password entry: polygon only"]
fn reactivating_an_account_leaves_a_hand_set_password_working_and_a_passwordless_one_alone() {
    // P7. The inverse control for the conditional unlock in
    // `AccountOperations::unsuspend`, and the reason the repair is not "skip the
    // unlock".
    //
    // Two accounts, because one can only ever show half of it:
    //
    // - the ordinary hosting account, which this agent never gives a password.
    //   Its shadow field is markers only (`!` on the Debian family, `!!` on the
    //   RHEL one, both measured), `usermod --lock` on it is a measured no-op,
    //   and `usermod --unlock` refuses it — exit 0 on Debian, **exit 1 on
    //   RHEL**. The resume must SUCCEED on both, which is the whole defect.
    // - an account somebody set a password on by hand, which is the only reason
    //   the account-level lock is worth applying at all. That password must
    //   really stop working while suspended and must come back afterwards. Had
    //   the fix been "never unlock", this half would fail and the customer would
    //   be locked out for good — which is why it is here.
    //
    // The assertions are on the raw shadow field, which is the byte string PAM
    // consults, read by a separate `getent` process rather than through the code
    // under test.
    let operations =
        AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro());

    let plain = PolygonAccount::create("polyunlockplain");
    let plain_before = shadow_password_field(plain.name().as_str());
    assert!(
        !plain_before.is_empty(),
        "a fresh account must not have an EMPTY password field: that is a login that \
         authenticates with no password at all"
    );
    operations
        .suspend(plain.name())
        .unwrap_or_else(|error| panic!("suspending the passwordless account: {error}"));
    operations.unsuspend(plain.name()).unwrap_or_else(|error| {
        panic!(
            "reactivating a passwordless account must succeed on BOTH families, and the \
             call this used to make exits 1 on the RHEL one: {error}"
        )
    });
    assert_eq!(
        shadow_password_field(plain.name().as_str()),
        plain_before,
        "a resume must not invent a credential for an account that never had one"
    );

    let credited = PolygonAccount::create("polyunlockcred");
    set_account_password(credited.name().as_str(), CUSTOMER_PASSWORD);
    let credited_before = shadow_password_field(credited.name().as_str());
    assert!(
        !credited_before.starts_with('!'),
        "the hand-set password must be USABLE before the suspension, or the lock below \
         proves nothing: {credited_before}"
    );

    operations
        .suspend(credited.name())
        .unwrap_or_else(|error| panic!("suspending the credited account: {error}"));
    let while_suspended = shadow_password_field(credited.name().as_str());
    assert!(
        while_suspended.starts_with('!'),
        "a suspension must really lock a password that really worked: {while_suspended}"
    );

    operations
        .unsuspend(credited.name())
        .unwrap_or_else(|error| panic!("reactivating the credited account: {error}"));
    assert_eq!(
        shadow_password_field(credited.name().as_str()),
        credited_before,
        "the resume must give back exactly the password the suspension took away: a lock \
         that cannot be lifted is a customer locked out for good"
    );
}

#[test]
#[ignore = "creates two real accounts and reads the real shadow database: polygon only"]
fn the_attestation_separates_a_hosting_login_from_one_locked_over_a_password() {
    // P8. The premise the PANEL's two handlers now rest on, measured on a real
    // host instead of argued from a fixture.
    //
    // The attestation carries two facts about one login. `login_locked` comes
    // from `passwd -S` and answers "can a password authenticate this", which is
    // what a suspension must know. `login_password` comes from the raw shadow
    // field and answers "is anything of the customer's held down", which is what
    // a reactivation must know. For an account with a hand-set password the two
    // agree; for an ordinary hosting account — which this agent never gives a
    // password — they do NOT, and the panel refused every reactivation there
    // was for as long as it decided from the first one.
    //
    // So both accounts must report `login_locked == true` here. If they ever
    // stop doing so together, this test is no longer observing the collapse it
    // exists for and says so in its own message.
    let operations =
        AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro());
    let sites = ProcessSiteHost::new();
    let cron = ProcessCronHost::new(polygon_distro());
    let logins = ProcessLoginsHost::new();

    let plain = PolygonAccount::create("polyattestplain");
    operations
        .suspend(plain.name())
        .unwrap_or_else(|error| panic!("suspending the passwordless account: {error}"));
    let plain_state = operations
        .suspension_state(&sites, &cron, &logins, plain.name())
        .unwrap_or_else(|error| panic!("the state must be readable: {error}"));

    let credited = PolygonAccount::create("polyattestcred");
    set_account_password(credited.name().as_str(), CUSTOMER_PASSWORD);
    operations
        .suspend(credited.name())
        .unwrap_or_else(|error| panic!("suspending the credited account: {error}"));
    let credited_state = operations
        .suspension_state(&sites, &cron, &logins, credited.name())
        .unwrap_or_else(|error| panic!("the state must be readable: {error}"));

    assert!(
        plain_state.login_locked && credited_state.login_locked,
        "`passwd -S` must report BOTH as locked, or this host does not collapse the two          states and this test is no longer observing what it exists for"
    );
    assert_eq!(
        plain_state.login_password,
        StoredPassword::Absent,
        "an ordinary hosting account holds markers and no hash: there is nothing to unlock,          and a panel refusing its reactivation refuses every account on the host. The raw          field was {:?}",
        shadow_password_field(plain.name().as_str()),
    );
    assert_eq!(
        credited_state.login_password,
        StoredPassword::Locked,
        "a suspended hand-set password IS held down, and a reactivation must keep refusing          until it comes back"
    );
}

/// Gives `username` a real password, the way an operator would.
///
/// Not through the agent: the point of the account it credits is that the agent
/// did NOT create the credential, and a test that set it through the code under
/// test would be asking that code to agree with itself.
fn set_account_password(username: &str, password: &str) {
    let mut child = Command::new(polygon_distro().chpasswd_binary())
        .stdin(std::process::Stdio::piped())
        .spawn()
        .expect("chpasswd must run in the polygon");
    child
        .stdin
        .as_mut()
        .expect("the pipe was just opened")
        .write_all(format!("{username}:{password}\n").as_bytes())
        .expect("chpasswd must accept the line");
    let status = child.wait().expect("chpasswd must finish");
    assert!(status.success(), "chpasswd must set the password: {status}");
}

/// The raw shadow password field the host holds for `username`.
///
/// Read with `getent`, the same instrument the operation uses, but through a
/// separate process here rather than through the code under test: the question
/// is what the HOST ended up holding, and asking `AccountOperations` would be
/// asking the change to confirm itself.
///
/// This is the byte string PAM consults, so an assertion on it is an assertion
/// about whether a password can authenticate — not a proxy for one.
fn shadow_password_field(username: &str) -> String {
    let entry = Command::new(polygon_distro().getent_binary())
        .arg("shadow")
        .arg(username)
        .output()
        .expect("getent must run in the polygon");
    assert!(
        entry.status.success(),
        "the account must have a shadow entry: {}",
        String::from_utf8_lossy(&entry.stderr)
    );
    String::from_utf8_lossy(&entry.stdout)
        .lines()
        .next()
        .unwrap_or_default()
        .split(':')
        .nth(1)
        .unwrap_or_default()
        .to_owned()
}

#[test]
#[ignore = "holds two real SFTP sessions open across a real suspension: polygon only"]
fn an_authenticated_sftp_session_is_ended_when_the_account_is_suspended() {
    // THIS TEST WAS RE-PINNED, DELIBERATELY, AND ITS PREVIOUS NAME SAID THE
    // OPPOSITE: `an_authenticated_sftp_session_keeps_transferring_after_the_
    // account_is_suspended`. That test measured today's behaviour and said in its
    // own message what a red line would mean — "the agent culls the account's
    // processes ... re-pin this test to the NEW behaviour instead of deleting it".
    // This is that re-pin. The owner asked for option 3 of
    // `docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`
    // § "The options, their costs, and a recommendation", and the cull is argued
    // in `docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`.
    //
    // What is measured, and why each part is here rather than assumed:
    //
    // * the session authenticates and TRANSFERS before the suspension — without
    //   that, a failure afterwards would be about this client;
    // * a NEW login is refused at the same moment — the old half of the promise,
    //   which the cull must not be allowed to replace;
    // * the OPEN session stops being served — the finding;
    // * a SECOND ACCOUNT'S open session goes on transferring after this
    //   account's suspension. That is the inverse control the threat note's
    //   reviewer list demands ("the cull cannot reach a uid other than the
    //   account's") AND the proof that the daemon was still serving sessions at
    //   the moment the first one died, so the death is the suspension's doing and
    //   not the harness's.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftplive");
    plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let neighbour = PolygonAccount::create("polysftpnear");
    plant_file(&neighbour);
    let neighbour_login = PolygonSftpLogin::create(&neighbour);
    let operations =
        AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro());

    let (mut session, greeting) = SftpControlSession::login(login.name(), CUSTOMER_PASSWORD);
    assert!(
        greeting.contains("Remote working directory:"),
        "the session must be authenticated BEFORE the suspension, or nothing below \
         is about a session that outlived one: {greeting}"
    );
    let (mut neighbour_session, neighbour_greeting) =
        SftpControlSession::login(neighbour_login.name(), CUSTOMER_PASSWORD);
    assert!(
        neighbour_greeting.contains("Remote working directory:"),
        "the neighbour's session must be authenticated too, or it cannot survive \
         anything: {neighbour_greeting}"
    );

    // The control that makes a later failure mean something: this client can
    // transfer through this session while the account is in good standing.
    let before = fetch_through(&mut session, "before");
    assert_eq!(
        before, CUSTOMER_CONTENT,
        "the open session must be able to transfer before the suspension, or a \
         failure afterwards would be about this client and not about the suspension"
    );

    // The two halves of a suspension, in the order the panel drives them. The
    // cull is inside the second one: locking a shadow field is read at
    // AUTHENTICATION and reaches no session that is already open.
    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("suspending the account must succeed: {error}"));
    let culled = set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));

    // The count, measured against a real `pkill` on a real host rather than
    // against a fake's arithmetic. It is what the operator's attestation states,
    // so it has to be a number this account's own processes could produce: at
    // least the session asserted open above, and never the neighbour's, whose
    // session is asserted to go on transferring below.
    let ended = culled
        .sessions_ended
        .expect("the locking direction must answer WITH a count, not merely succeed");
    assert!(
        ended >= 1,
        "the cull must report the session this test held open: got {ended}"
    );

    // The control that makes the rest of this test mean something: the
    // suspension REALLY landed on this host at this moment.
    assert!(
        shadow_password_field(login.name()).starts_with('!'),
        "the suspension must have marked the shadow field before this test can \
         claim anything about the session behind it"
    );
    let new_session = sshd.sftp(login.name(), CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !new_session.status.success(),
        "a NEW session with the same credential must be refused while the account \
         is suspended, or the suspension is not in force at all:\n{}",
        said(&new_session)
    );

    // THE FINDING. The daemon no longer serves the session that was open: the
    // process carrying it ran as the account's uid and the suspension ended it,
    // so the client's channel is gone and the `pwd` this type terminates every
    // reply with can never come back.
    // The question is asked by MOVING BYTES and not by `pwd`: the openssh client
    // answers `pwd` out of its own cached state, so it keeps answering after the
    // daemon has closed the connection (measured — see
    // `fixtures/sftp_control_session.rs`). A transfer cannot be faked that way:
    // either the file lands or the session is gone.
    let (after, landed) = fetch_attempt(&mut session, "after");
    assert!(
        landed.is_none(),
        "MEASURED: suspending an account ends the SFTP session it already had \
         open. THIS LINE GOING RED means a suspended customer's live session \
         still moved their data — check that `end_account_sessions` ran, that it \
         named this account's uid, and that `pkill` is installed on this image. \
         The session said: {after}"
    );

    // The cross-tenant inverse control, and the harness's own liveness proof: a
    // session belonging to a DIFFERENT account is untouched and still moves
    // bytes, at the same moment, through the same daemon.
    let neighbour_after = fetch_through(&mut neighbour_session, "neighbour-after");
    assert_eq!(
        neighbour_after, CUSTOMER_CONTENT,
        "a cull that reached another account's uid would be a cross-tenant \
         denial of service, and a daemon that had stopped serving every session \
         would make the assertion above vacuous"
    );

    // And the reversal, so the account is not left suspended for a later test.
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        polygon_distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("reactivating the account must succeed: {error}"));

    drop(session);
    drop(neighbour_session);
    drop(login);
    drop(neighbour_login);
}

/// Fetches the planted file through an already-open `session` and returns the
/// bytes that landed.
///
/// The assertion is on the RETRIEVED FILE and not on what the client printed: a
/// client that reported `Fetching` and wrote nothing would satisfy a check on
/// its output, and the question is whether a suspended customer's data still
/// moves.
///
/// # Panics
///
/// Panics when the download landed nowhere, quoting what the session said —
/// which is the outcome that would mean the daemon HAD closed the session.
fn fetch_through(session: &mut SftpControlSession, tag: &str) -> String {
    let (said, landed) = fetch_attempt(session, tag);

    landed.unwrap_or_else(|| {
        panic!(
            "the transfer through the open session landed nothing; \
             the session said: {said}"
        )
    })
}

/// Tries to fetch the planted file through `session`, and reports BOTH what the
/// session said and whether any bytes landed.
///
/// The fallible half of [`fetch_through`], and the two are one function because
/// the cull test needs the same transfer to come back EMPTY while every other
/// caller needs it to come back full — the rule of two applied to a measurement
/// whose two directions must not be allowed to drift apart. A test asserting the
/// session is gone and a test asserting it works therefore ask the daemon exactly
/// the same question.
///
/// `probe_command` rather than `command`, because after the cull the client
/// process is dead and the WRITE is what fails; `command` panics there by design.
fn fetch_attempt(session: &mut SftpControlSession, tag: &str) -> (String, Option<String>) {
    let landing = std::env::temp_dir().join(format!("maran-sftp-live-{tag}.txt"));
    let _ = std::fs::remove_file(&landing);

    let said = session.probe_command(&format!(
        "get /home/{CUSTOMER_FILE} {}",
        landing.to_str().expect("a utf-8 path")
    ));

    (said, std::fs::read_to_string(&landing).ok())
}

/// The daemon binary, asked for its own reading of the configuration it serves.
///
/// The same path `polygon_sshd.rs` starts the daemon from, spelled again here
/// because that constant is private to the fixture and this is the one test
/// that interrogates the binary rather than the daemon it started.
const SSHD_BINARY_FOR_CONFIG_DUMP: &str = "/usr/sbin/sshd";

/// The effective sshd configuration for `login`, as the daemon itself resolves it.
///
/// `sshd -T` prints the configuration the daemon would use, every keyword
/// resolved to one value, defaults included. `-C` supplies the connection
/// attributes the `Match` blocks are evaluated against, so what comes back is
/// the configuration that governs THIS login and not the file's global half —
/// which matters here because everything the installer wrote for SFTP lives
/// inside a `Match Group` block.
///
/// # Panics
///
/// Panics when the daemon refuses to dump the configuration, quoting what it
/// said: a failed dump measures nothing and must never read as an absent value.
fn effective_sshd_configuration(login: &str, extra: &[&str]) -> String {
    let mut command = Command::new(SSHD_BINARY_FOR_CONFIG_DUMP);
    command.arg("-T").arg("-C").arg(format!(
        "user={login},host=localhost,addr=127.0.0.1,laddr=127.0.0.1,lport=22"
    ));
    command.args(extra);

    let dumped = command.output().unwrap_or_else(|error| {
        panic!("the polygon image installs {SSHD_BINARY_FOR_CONFIG_DUMP}: {error}")
    });
    assert!(
        dumped.status.success(),
        "sshd must be able to dump its effective configuration for {login}; \
         without it this test has measured nothing:\n{}",
        String::from_utf8_lossy(&dumped.stderr)
    );
    String::from_utf8_lossy(&dumped.stdout).to_lowercase()
}

/// The single value `key` has in an `sshd -T` dump, or `None` when it is absent.
///
/// `None` and `Some("0")` are deliberately different answers. A keyword the
/// daemon did not print is a keyword this test cannot reason about, and
/// collapsing the two would let a dump that lost a line pass as a measurement
/// of a disabled bound.
fn sshd_setting(dump: &str, key: &str) -> Option<String> {
    dump.lines()
        .filter_map(|line| line.split_once(' '))
        .find(|(name, _)| *name == key)
        .map(|(_, value)| value.trim().to_owned())
}

#[test]
#[ignore = "asks the installed sshd for its effective configuration: polygon only"]
fn the_sshd_configuration_this_product_writes_puts_no_idle_bound_on_an_sftp_session() {
    // THIS TEST PINS TODAY'S CONFIGURATION; IT DOES NOT ENDORSE IT.
    //
    // `an_authenticated_sftp_session_is_ended_when_the_account_is_suspended`
    // above, and its FTPS counterpart, now measure that a SUSPENDED account's
    // open session is culled. This case is about every session that is NOT
    // suspended — the ordinary customer who walked away from a client — and there
    // the two protocols DO differ, which is the finding it exists for. Suspension
    // no longer rests on this difference, and that is exactly why the difference
    // still has to be observed: with the cull in place it is easy to read an idle
    // bound as somebody else's problem. FTPS carries
    // `idle_session_timeout=600` in the configuration the agent renders, pinned
    // by `ftps_on_a_real_host.rs::the_vsftpd_configuration_this_product_writes_bounds_an_idle_ftps_session_at_ten_minutes`.
    // SFTP carries no bound at all, and the absence is a DEFAULT rather than a
    // choice this product made: `grep -rn "ClientAlive" installer/
    // agent/crates/templates/templates/` returns nothing, so the value below is
    // whichever OpenSSH ships. It is measured here rather than quoted, because a
    // distribution default is not a bound this product guarantees and a future
    // OpenSSH could change it without anybody here noticing.
    //
    // The options and their costs are in
    // `docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`
    // § "The options, their costs, and a recommendation".
    // The value of this test is that the day either half moves, it goes red BY
    // NAME and the change is a decision rather than an accident.
    let _sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpidle");
    let login = PolygonSftpLogin::create(&account);

    let dump = effective_sshd_configuration(login.name(), &[]);

    // THE POSITIVE CONTROL, on the axis that can go blind. Everything the
    // installer wrote lives inside `Match Group maran-sftp`; if `-C` had matched
    // nothing, the dump would be the distribution's global half and would still
    // report no idle bound, so the assertion below would pass while measuring a
    // configuration no customer is ever served. These two directives exist
    // ONLY in that block (`installer/lib/86-sftp.sh:121-130`), so their
    // presence is the proof that the block was evaluated for this login.
    assert!(
        dump.contains("forcecommand internal-sftp"),
        "the dump must be the configuration in force for an SFTP login — the \
         installer's `Match Group` block — or the absence of a bound below is \
         measured in the wrong half of the file:\n{dump}"
    );
    assert!(
        sshd_setting(&dump, "chrootdirectory").is_some(),
        "and the same block's chroot must be in force for this login:\n{dump}"
    );

    // THE VACUITY GUARD. A keyword the daemon did not print is not a keyword set
    // to zero, and reading it as one would turn a truncated dump into evidence.
    let interval = sshd_setting(&dump, "clientaliveinterval").unwrap_or_else(|| {
        panic!("sshd -T always prints clientaliveinterval; a dump without it has measured nothing:\n{dump}")
    });
    let count_max = sshd_setting(&dump, "clientalivecountmax")
        .unwrap_or_else(|| panic!("sshd -T always prints clientalivecountmax:\n{dump}"));
    let keepalive = sshd_setting(&dump, "tcpkeepalive").unwrap_or_default();

    // THE FINDING.
    assert_eq!(
        interval, "0",
        "MEASURED: the sshd configuration this product writes puts NO idle bound \
         on an authenticated SFTP session — ClientAliveInterval is 0, which is \
         OpenSSH's own default and not a value this repository sets anywhere. So \
         an open SFTP session outlives a suspension for as long as the client \
         keeps the connection, with no ten-minute counterpart to FTPS's \
         idle_session_timeout. If this line is red the configuration has CHANGED, \
         which may well be the right change — make it a decision: \
         clientalivecountmax={count_max}, tcpkeepalive={keepalive}"
    );

    // `TCPKeepAlive yes` is measured and named so nobody mistakes it for the
    // missing bound. It probes whether the PEER is still reachable and drops a
    // session whose network has gone away; a suspended customer sitting on a
    // healthy connection answers every probe, so it bounds nothing about this.
    assert_eq!(
        keepalive, "yes",
        "TCPKeepAlive is recorded because a reader looking for the absent bound \
         will find it and must be told it is not one: it detects an unreachable \
         peer, not an idle one"
    );

    // THE INVERSE CONTROL. A probe that reports "no bound" for every input has
    // proved nothing about this configuration. Hand the SAME instrument a
    // daemon that DOES carry a bound and require it to say so.
    let with_a_bound =
        effective_sshd_configuration(login.name(), &["-o", "ClientAliveInterval=300"]);
    assert_eq!(
        sshd_setting(&with_a_bound, "clientaliveinterval").as_deref(),
        Some("300"),
        "the inverse control: this instrument must REPORT a bound when one is in \
         force, or its answer above is blind:\n{with_a_bound}"
    );
    assert!(
        with_a_bound.contains("forcecommand internal-sftp"),
        "and the inverse control must still be reading the product's own block"
    );

    drop(login);
}
