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
// Shared by both SFTP suites; each uses only part of it, so the allow sits on
// the module and an item no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_sftp_login.rs"]
mod polygon_sftp_login;

use std::os::unix::fs::MetadataExt as _;
use std::path::{Path, PathBuf};
use std::process::Command;

use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_agent_core::validation::system::ssh_public_key::SshPublicKey;
use maran_ops::logins::{ProcessLoginsHost, set_account_logins_locked};
use maran_ops::sftp::{
    AuthorizedKeySet, ProcessSftpHost, SftpError, SftpUserRequest, create_sftp_user,
    delete_sftp_user, set_sftp_authorized_keys, set_sftp_password,
};

use polygon_account::PolygonAccount;
use polygon_sftp_login::PolygonSftpLogin;
use polygon_sshd::PolygonSshd;

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
    let jail = PolygonSftpLogin::jail_of(&account);

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
    let planted = PolygonSftpLogin::plant_file(&account);
    let login = PolygonSftpLogin::create(&account);
    let jail = PolygonSftpLogin::jail_of(account.name());

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
    let inside = Path::new(jail.mount_point()).join(PolygonSftpLogin::CUSTOMER_FILE);
    assert_eq!(
        std::fs::read_to_string(&inside).unwrap_or_default(),
        PolygonSftpLogin::CUSTOMER_CONTENT,
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
    PolygonSftpLogin::plant_file(&mine);
    let stolen_from = PolygonSftpLogin::plant_file(&neighbour);
    let login = PolygonSftpLogin::create(&mine);

    // 1. The login works, with the password the agent set, and lands in the
    //    jail rather than anywhere on the host's filesystem. `/` in the session
    //    IS the chroot: what it lists is what a customer can see at all.
    let session = sshd.sftp(
        login.name(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
        "pwd\nls\ncd home\nls\n",
    );
    assert!(
        session.status.success(),
        "the login must work with the password the agent set:\n{}",
        PolygonSftpLogin::said(&session)
    );
    let transcript = PolygonSftpLogin::said(&session);
    assert!(
        transcript.contains("Remote working directory: /"),
        "the session must start at the root of its chroot:\n{transcript}"
    );
    assert!(
        transcript.contains(PolygonSftpLogin::CUSTOMER_FILE),
        "the account's own file must be visible through the bind mount:\n{transcript}"
    );

    // 1b. And a password that is NOT the one the agent set is refused. Without
    //     this, every assertion in this file would also hold on a daemon that
    //     let anybody in, and "the credential works" would mean nothing.
    let wrong = sshd.sftp(login.name(), "Wr0ng-password", "pwd\n");
    assert!(
        !wrong.status.success(),
        "the daemon must refuse a password that is not the one that was set:\n{}",
        PolygonSftpLogin::said(&wrong)
    );

    // 2. The chroot, asserted as a refusal in a real session. `/etc` exists on
    //    this host and is world-readable; inside the chroot there is no such
    //    path at all, which is what "chrooted" means and what a `ChrootDirectory`
    //    line in the wrong block would not achieve.
    let escape = sshd.sftp(
        login.name(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
        "cd /etc\n",
    );
    assert!(
        !escape.status.success(),
        "an SFTP login must not be able to leave its jail:\n{}",
        PolygonSftpLogin::said(&escape)
    );
    let refusal = PolygonSftpLogin::said(&escape);
    assert!(
        refusal.contains("No such file") || refusal.contains("not found"),
        "the refusal must be the path not existing inside the chroot, not some \
         other failure:\n{refusal}"
    );

    // 3. The neighbour's file, by absolute path. It is readable to root on this
    //    host and belongs to a different account; from inside the chroot the
    //    path does not resolve, and nothing is fetched.
    let neighbour_path = format!(
        "/home/{}/{}",
        neighbour.name().as_str(),
        PolygonSftpLogin::CUSTOMER_FILE
    );
    let landing = std::env::temp_dir().join("maran-polygon-stolen.txt");
    let _ = std::fs::remove_file(&landing);
    let theft = sshd.sftp(
        login.name(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
        &format!("get {neighbour_path} {}\n", landing.display()),
    );
    assert!(
        !theft.status.success(),
        "an SFTP login must not reach another account's files:\n{}",
        PolygonSftpLogin::said(&theft)
    );
    assert!(
        !landing.exists(),
        "nothing may have been fetched from another account"
    );
    // The neighbour's file is genuinely there and genuinely worth having, so
    // the refusal above is a refusal and not a test that asked for nothing.
    assert_eq!(
        std::fs::read_to_string(&stolen_from).unwrap_or_default(),
        PolygonSftpLogin::CUSTOMER_CONTENT
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
    std::fs::write(&source, PolygonSftpLogin::CUSTOMER_CONTENT).expect("a local file to upload");

    let session = sshd.sftp(
        login.name(),
        PolygonSftpLogin::CUSTOMER_PASSWORD,
        &format!("cd home\nput {} uploaded.txt\n", source.display()),
    );
    assert!(
        session.status.success(),
        "a customer must be able to upload into its own home:\n{}",
        PolygonSftpLogin::said(&session)
    );

    // The file arrived in the REAL home, through the bind mount, and it belongs
    // to the account rather than to some identity of the login's own. That last
    // part is what makes the login usable at all: a file owned by anything else
    // is a file the account's own php-fpm pool cannot open.
    let uploaded = account.home().join("uploaded.txt");
    assert_eq!(
        std::fs::read_to_string(&uploaded).unwrap_or_default(),
        PolygonSftpLogin::CUSTOMER_CONTENT,
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
    let attempt = sshd.exec(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "whoami");
    assert!(
        !attempt.status.success(),
        "an SFTP login must not be able to execute a command:\n{}",
        PolygonSftpLogin::said(&attempt)
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
    let refusal = PolygonSftpLogin::said(&attempt);
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
        password: Password::parse(PolygonSftpLogin::CUSTOMER_PASSWORD).expect("a valid password"),
    };

    let refused = create_sftp_user(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        &request,
    );
    assert!(
        matches!(refused, Err(SftpError::AccountMissing)),
        "a login for an account that does not exist must be refused, got {refused:?}"
    );

    // And nothing was built on the way to that refusal. A jail left behind for a
    // non-existent account is a root-owned directory nothing will ever mount
    // into, and a login created against it could read nothing at all.
    let jail = PolygonSftpLogin::jail_of(&account);
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
        PolygonSftpLogin::distro(),
        &SftpUserRequest {
            account: account.name().clone(),
            user: user.clone(),
            password: Password::parse(PolygonSftpLogin::SECOND_PASSWORD).expect("a valid password"),
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
    let original = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        original.status.success(),
        "the first password must still work after a repeated create:\n{}",
        PolygonSftpLogin::said(&original)
    );
    let unused = sshd.sftp(login.name(), PolygonSftpLogin::SECOND_PASSWORD, "pwd\n");
    assert!(
        !unused.status.success(),
        "the repeat's password must never have been set:\n{}",
        PolygonSftpLogin::said(&unused)
    );

    // 2. A reset DOES change it, and the old value stops working. This is the
    //    one operation here whose whole purpose is to replace a credential, and
    //    a reset that merely added one would look identical from the panel.
    set_sftp_password(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        &user,
        &Password::parse(PolygonSftpLogin::SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("resetting the password must succeed: {error}"));

    let renewed = sshd.sftp(login.name(), PolygonSftpLogin::SECOND_PASSWORD, "pwd\n");
    assert!(
        renewed.status.success(),
        "the login must work with the reset password:\n{}",
        PolygonSftpLogin::said(&renewed)
    );
    let stale = sshd.sftp(login.name(), PolygonSftpLogin::CUSTOMER_PASSWORD, "pwd\n");
    assert!(
        !stale.status.success(),
        "the replaced password must no longer authenticate:\n{}",
        PolygonSftpLogin::said(&stale)
    );

    // 3. Repeating the reset succeeds and leaves the same value working.
    set_sftp_password(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        &user,
        &Password::parse(PolygonSftpLogin::SECOND_PASSWORD).expect("a valid password"),
    )
    .unwrap_or_else(|error| panic!("a repeated reset must succeed: {error}"));
    let after = sshd.sftp(login.name(), PolygonSftpLogin::SECOND_PASSWORD, "pwd\n");
    assert!(
        after.status.success(),
        "a repeated reset must leave the same password working:\n{}",
        PolygonSftpLogin::said(&after)
    );

    // 4. The deletion, then the deletion again. The fixture's teardown performs
    //    the first one and takes the account's mount down with it, so the second
    //    is asked of a host that really no longer has the login.
    drop(login);

    let again = delete_sftp_user(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
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

/// A fresh Ed25519 key pair in a temporary directory: the private half's path,
/// and the public half as `set_sftp_authorized_keys` takes it.
fn polygon_key_pair(directory: &Path) -> (PathBuf, AuthorizedKeySet) {
    let identity = directory.join("id_ed25519");
    let made = Command::new("ssh-keygen")
        .args(["-q", "-t", "ed25519", "-N", "", "-C", "polygon@test", "-f"])
        .arg(&identity)
        .output()
        .expect("the polygon image ships ssh-keygen");
    assert!(
        made.status.success(),
        "ssh-keygen: {}",
        PolygonSftpLogin::said(&made)
    );

    let public = std::fs::read_to_string(identity.with_extension("pub")).expect("a public key");
    let key = SshPublicKey::parse(public.trim()).expect("ssh-keygen writes an accepted key");

    (identity, AuthorizedKeySet::new(vec![key]).expect("one key"))
}

/// Replaces `login`'s key set, panicking with the agent's answer on refusal.
fn set_keys(login: &PolygonSftpLogin, keys: &AuthorizedKeySet) {
    set_sftp_authorized_keys(
        &ProcessSftpHost::new(),
        PolygonSftpLogin::distro(),
        &login.account,
        &login.user,
        keys,
    )
    .unwrap_or_else(|error| panic!("setting the login's keys must succeed: {error}"));
}

#[test]
#[ignore = "authenticates with a real key against a real sshd: polygon only"]
fn a_key_logs_in_and_is_refused_once_revoked_or_suspended() {
    // Issue #135's closing criterion: a real login with a key, and a real
    // refusal after the key is revoked. The suspension half is the hole the
    // design exists to close: with `UsePAM yes`, sshd does not consult a locked
    // password for a key.
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create("polysftpkeys");
    let login = PolygonSftpLogin::create(&account);
    let scratch = tempfile::tempdir().expect("a scratch directory");
    let (identity, keys) = polygon_key_pair(scratch.path());

    // Inverse control: before any key is set, the key must NOT work, or the
    // success below would prove nothing about the file this code wrote.
    let unset = sshd.sftp_with_key(login.name(), &identity, "pwd\n");
    assert!(
        !unset.status.success(),
        "no key is set yet:\n{}",
        PolygonSftpLogin::said(&unset)
    );

    set_keys(&login, &keys);
    let with_key = sshd.sftp_with_key(login.name(), &identity, "pwd\n");
    assert!(
        with_key.status.success(),
        "the login must authenticate with its key:\n{}",
        PolygonSftpLogin::said(&with_key)
    );

    set_keys(
        &login,
        &AuthorizedKeySet::new(Vec::new()).expect("an empty set"),
    );
    let revoked = sshd.sftp_with_key(login.name(), &identity, "pwd\n");
    assert!(
        !revoked.status.success(),
        "a revoked key must be refused:\n{}",
        PolygonSftpLogin::said(&revoked)
    );

    set_keys(&login, &keys);
    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        true,
    )
    .unwrap_or_else(|error| panic!("locking the account's logins must succeed: {error}"));
    let suspended = sshd.sftp_with_key(login.name(), &identity, "pwd\n");
    assert!(
        !suspended.status.success(),
        "a suspended login's key must be refused:\n{}",
        PolygonSftpLogin::said(&suspended)
    );

    set_account_logins_locked(
        &ProcessLoginsHost::new(),
        PolygonSftpLogin::distro(),
        account.name(),
        false,
    )
    .unwrap_or_else(|error| panic!("unlocking the account's logins must succeed: {error}"));
    let resumed = sshd.sftp_with_key(login.name(), &identity, "pwd\n");
    assert!(
        resumed.status.success(),
        "resuming must give the key back:\n{}",
        PolygonSftpLogin::said(&resumed)
    );

    drop(login);
}
