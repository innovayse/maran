//! FTPS against a real vsftpd, which is the only place `ops::ftps` means
//! anything.
//!
//! Everything this area does is a claim about what a daemon, a PAM stack and a
//! kernel will do with what the agent wrote, and a fake can confirm none of it.
//! What is settled here and nowhere else:
//!
//! - **Forced TLS.** Asserted as a REFUSAL in a real session — a client that
//!   will not negotiate is told `530 Non-anonymous sessions must use
//!   encryption` — never as `force_local_logins_ssl=YES` appearing in a file.
//!   A key in the wrong place reads the same and does nothing, and vsftpd takes
//!   the LAST occurrence of a key, so one appended line switches it off on a
//!   configuration that still parses, still starts and still greets.
//! - **The group is the whole authorization.** A system login with a CORRECT
//!   password and no membership of the FTPS group is refused by the PAM stack
//!   the installer wrote. This is the assertion the installer's own polygon
//!   check declared UNOBSERVED.
//! - **The chroot.** A refusal in a real session, and a listing that shows the
//!   account's `home` and nothing else.
//! - **The credential, and the identity behind it.** The login authenticates
//!   with the password the agent handed `chpasswd`, and a file it uploads lands
//!   in the account's real home owned by the ACCOUNT — which is what
//!   `useradd --non-unique --uid` is for and what a fake cannot show.
//! - **The suspension, and the password change inside it.** A locked login is
//!   refused by the daemon; a password change while it is locked leaves it
//!   refused; and the password the customer chose is the one that works once the
//!   account is resumed.
//! - **A password change authorised for one account cannot reach another
//!   account's login**, whatever the two names decompose to.
//!
//! The daemon reads the `vsftpd.conf` the AGENT rendered and swapped in through
//! `ops::safe_write`, and the PAM stack `installer/lib/89-ftps.sh` wrote when
//! the image was built. Nothing here writes a line of either.
//!
//! These tests need `docker run --privileged`: the jail's bind mount is a real
//! mount. Without it `create_ftps_user` returns `JailFailed` and they fail
//! loudly — they never pass on a jail that was never filled.
//!
//! **UNOBSERVED HERE: the service manager**, and it is stated in
//! `fixtures/polygon_vsftpd.rs` in full. The polygon images ship a `systemctl`
//! shim that records a unit's state and starts nothing, so the restart
//! `enable_ftps` performs is answered by a script; the fixture runs the unit's
//! own `ExecStart=` line instead. The configuration and the daemon are real;
//! the thing that would launch them on a server is not.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/ftps_control_session.rs"]
mod ftps_control_session;
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_certificate_authority.rs"]
mod polygon_certificate_authority;
#[path = "fixtures/polygon_vsftpd.rs"]
mod polygon_vsftpd;
#[path = "fixtures/retrieved_file.rs"]
mod retrieved_file;

use std::io::Write as _;
use std::os::unix::fs::MetadataExt as _;
use std::path::PathBuf;
use std::process::{Command, Stdio};

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;
use maran_agent_core::validation::web::port::Port;
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::cron::ProcessCronHost;
use maran_ops::ftps::{
    FtpsConfiguration, FtpsError, FtpsUserRequest, ProcessFtpsHost, create_ftps_user,
    get_ftps_status, remove_account_ftps, set_ftps_password,
};
use maran_ops::logins::{LoginProtocol, ProcessLoginsHost, set_account_logins_locked};
use maran_ops::sites::{ProcessSiteHost, SiteCertificate};

use ftps_control_session::FtpsControlSession;
use polygon_account::PolygonAccount;
use polygon_certificate_authority::PolygonCertificateAuthority;
use polygon_vsftpd::PolygonVsftpd;

/// The hostname every test in this suite serves FTPS for.
const HOSTNAME: &str = "ftps.polygon.test";

/// The password every login in this suite is created with.
///
/// It uses every character class `Password` allows — letters, digits and
/// `-_.=+`. A password made only of letters would not notice `chpasswd`, PAM or
/// a pipe eating the punctuation, and the failure would be a customer who cannot
/// log in with what the panel showed them.
const CUSTOMER_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The password a deliberate rotation sets.
const ROTATED_PASSWORD: &str = "Rotated-9.password=+_";

/// The login suffix every account in this suite is given.
const LOGIN_SUFFIX: &str = "files";

/// The file each account is given in its home, so a session has something to
/// find.
const CUSTOMER_FILE: &str = "hello.txt";

/// What that file holds.
const CUSTOMER_CONTENT: &str = "customer data\n";

/// The name the bind mount carries inside the jail, and therefore the first path
/// component of everything a session sees.
const HOME_INSIDE_THE_JAIL: &str = "home";

/// The greeting fragment vsftpd answers a plaintext login attempt with.
///
/// Measured on both polygon families. Asserted as this sentence rather than as a
/// bare `530`, because a wrong password is a `530` too and the two refusals are
/// the two halves this suite has to tell apart.
const ENCRYPTION_REQUIRED: &str = "530 Non-anonymous sessions must use encryption";

/// What vsftpd answers a login it accepted.
const LOGIN_SUCCESSFUL: &str = "230 Login successful";

/// What a refusal of a credential this suite knows to be correct most likely
/// means, printed beside every such assertion.
///
/// This began as a claim about the RHEL-family image and was WRONG about the
/// subject, which is worth saying because the wrong sentence is what stops the
/// next reader looking. The first measurement — `pam_unix.so` refusing a correct
/// password on `maran-polygon-alma9` while `pam_permit.so` accepts it — was
/// real, but the cause is not the image's PAM stack and not the agent. It is the
/// HOST's AppArmor policy reaching into an unconfined container:
/// `/etc/apparmor.d/unix-chkpwd` attaches by PATH to `/usr/sbin/unix_chkpwd`,
/// grants `/etc/shadow r` and withholds `capability dac_override`, and
/// AlmaLinux's `/etc/shadow` is `0000 root:root`, so the helper `pam_unix` reads
/// the password through cannot open it and answers PAM_AUTHINFO_UNAVAIL for
/// every login. `--privileged` implies `apparmor=unconfined`, which is precisely
/// when a host path profile attaches, and `scripts/lib/polygon.sh` runs the
/// whole family privileged. Ubuntu's `0640 root:shadow` is readable by its
/// OWNER, so the Debian image never showed it.
///
/// `docker/polygon/alma9.Dockerfile` closes it by making that file `0400
/// root:root` — readable by root alone, with no capability — and
/// [`the_shadow_database_can_be_read_for_authentication_in_this_container`] is
/// the test that fails by name if the condition ever comes back. So a refusal of
/// a correct credential here is once again what it should be: a defect in the
/// code under test.
const PAM_ON_THIS_IMAGE: &str = "\
    A refusal of a credential this suite knows to be correct is a defect of the \
    agent, UNLESS the harness itself cannot read the shadow database — which is \
    a separate, named test in this file, \
    the_shadow_database_can_be_read_for_authentication_in_this_container. If \
    that test is red too, read it first: nothing else in this suite means \
    anything while it is.";

/// The helper `pam_unix.so` verifies a shadow password through.
///
/// The same absolute path on both polygon families (measured), which is why it
/// is written here rather than asked of the `distro` adapter: the adapter
/// carries the platform facts the AGENT depends on, and the agent does not
/// depend on this one. This suite does, for the length of one probe.
const SHADOW_PASSWORD_HELPER: &str = "/usr/sbin/unix_chkpwd";

/// A password the probe below offers, and that no account in any image holds.
///
/// The probe needs a login that EXISTS and a password that is WRONG, because
/// the two answers it has to tell apart are "the shadow entry was read and this
/// password does not match it" and "the shadow entry could not be read at all".
/// It therefore needs no secret: `root`'s shadow field is `*` in both images.
const A_PASSWORD_NO_ACCOUNT_HAS: &str = "not-the-password-of-any-account-in-this-image";

/// `PAM_AUTH_ERR` — the helper read the shadow database and refused this
/// password. The healthy answer to the probe.
const PAM_AUTH_ERR: i32 = 7;

/// `PAM_AUTHINFO_UNAVAIL` — the helper could not retrieve the authentication
/// information at all, so no password would have worked. The broken answer.
const PAM_AUTHINFO_UNAVAIL: i32 = 9;

/// The passive data range the suite enables with.
const PASSIVE_MIN: u32 = 30000;

/// The top of that range.
const PASSIVE_MAX: u32 = 30099;

/// The concurrent-session ceiling the suite enables with.
const MAX_CLIENTS: u32 = 20;

/// The configuration every test applies.
fn configuration() -> FtpsConfiguration {
    FtpsConfiguration {
        hostname: hostname(),
        passive_port_min: Port::parse(PASSIVE_MIN).expect("a valid port"),
        passive_port_max: Port::parse(PASSIVE_MAX).expect("a valid port"),
        passive_address: None,
        max_clients: MAX_CLIENTS,
    }
}

/// The suite's hostname, validated.
fn hostname() -> Domain {
    Domain::parse(HOSTNAME).expect("the suite's own hostname must be valid")
}

/// One real FTPS login, created by the code under test and revoked when the test
/// ends.
///
/// The teardown is what this type adds: the login is removed and the account's
/// bind mount taken down through `remove_account_ftps`, so one test's mount
/// cannot be what a later test is really looking at.
struct PolygonFtpsLogin {
    /// The login's validated name.
    user: FtpsUserName,
    /// The account it belongs to, kept so the teardown asks about the right one.
    account: AccountName,
}

impl PolygonFtpsLogin {
    /// Creates `account`'s login through `create_ftps_user`.
    ///
    /// # Panics
    ///
    /// Panics when the operation refuses — including on a host where the bind
    /// mount could not be made, which is `JailFailed` and means the container
    /// was started without `--privileged`.
    fn create(account: &AccountName, password: &str) -> Self {
        Self::create_with_suffix(account, LOGIN_SUFFIX, password)
    }

    /// Creates `account`'s login under a suffix of the test's choosing.
    ///
    /// # Panics
    ///
    /// Panics for the reasons [`Self::create`] does.
    fn create_with_suffix(account: &AccountName, suffix: &str, password: &str) -> Self {
        let user = FtpsUserName::for_account(account, suffix).expect("a valid login name");
        let request = FtpsUserRequest {
            account: account.clone(),
            user: user.clone(),
            password: Password::parse(password).expect("the suite's own password must be valid"),
        };

        create_ftps_user(&ProcessFtpsHost::new(), PolygonVsftpd::distro(), &request)
            .unwrap_or_else(|error| panic!("the login must be created in the polygon: {error}"));

        Self {
            user,
            account: account.clone(),
        }
    }

    /// The login's full system name, as the password database spells it.
    fn name(&self) -> &str {
        self.user.as_str()
    }

    /// The login's validated name.
    fn user(&self) -> &FtpsUserName {
        &self.user
    }
}

impl Drop for PolygonFtpsLogin {
    /// Revokes the login and takes the account's jail down, panic or not.
    fn drop(&mut self) {
        if let Err(error) = remove_account_ftps(
            &ProcessFtpsHost::new(),
            PolygonVsftpd::distro(),
            &self.account,
        ) {
            eprintln!(
                "the polygon account {}'s ftps could not be removed: {error}",
                self.account.as_str()
            );
        }
    }
}

/// Puts `CUSTOMER_FILE` in the account's home, owned by the account.
///
/// # Panics
///
/// Panics when the file cannot be written or chowned.
fn give_the_account_a_file(account: &PolygonAccount) -> PathBuf {
    let path = account.home().join(CUSTOMER_FILE);
    std::fs::write(&path, CUSTOMER_CONTENT).expect("the account's home is writable by root");

    let ids = account.ids();
    let outcome = Command::new("/usr/bin/chown")
        .arg(format!("{}:{}", ids.uid(), ids.gid()))
        .arg(&path)
        .output()
        .expect("the polygon image installs chown");
    assert!(
        outcome.status.success(),
        "the file must belong to the account"
    );

    path
}

/// Runs `curl` with the suite's own flags and returns everything it printed.
///
/// `-v` because the assertions are about the SERVER's replies, and curl decrypts
/// the control channel: its verbose log carries `230 Login successful` and
/// `530 …` verbatim, which a status code cannot distinguish. `-k` because the
/// material this suite places is self-signed, and verifying a chain is not what
/// any of these tests are about.
fn curl(arguments: &[&str]) -> String {
    let outcome = Command::new("/usr/bin/curl")
        .args([
            "-sS",
            "-v",
            "-k",
            "--connect-timeout",
            "10",
            "--max-time",
            "60",
        ])
        .args(arguments)
        .output()
        .expect("the polygon image installs curl");

    format!(
        "{}{}",
        String::from_utf8_lossy(&outcome.stdout),
        String::from_utf8_lossy(&outcome.stderr)
    )
}

/// A credential as curl takes it.
fn credential(login: &str, password: &str) -> String {
    format!("{login}:{password}")
}

/// Lists the session's own root over TLS and returns ONLY the entries.
///
/// Without `-v`: the verbose log is what the refusal assertions read, and it
/// interleaves curl's own `{ [n bytes data]` progress lines with the listing, so
/// a test asking "what does the session's root hold" has to ask a client that is
/// printing nothing else.
fn entries_over_tls(login: &str, password: &str) -> Vec<String> {
    let outcome = Command::new("/usr/bin/curl")
        .args(["-sS", "-k", "--connect-timeout", "10", "--max-time", "60"])
        .args([
            "--ssl-reqd",
            "-u",
            &credential(login, password),
            "ftp://127.0.0.1/",
            "--list-only",
        ])
        .output()
        .expect("the polygon image installs curl");

    String::from_utf8_lossy(&outcome.stdout)
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty())
        .map(str::to_owned)
        .collect()
}

/// Lists the session's own root over a TLS session, and returns what curl saw.
fn list_over_tls(login: &str, password: &str) -> String {
    curl(&[
        "--ssl-reqd",
        "-u",
        &credential(login, password),
        "ftp://127.0.0.1/",
        "--list-only",
    ])
}

/// Lists the session's own root over PLAIN ftp, and returns what curl saw.
fn list_without_tls(login: &str, password: &str) -> String {
    curl(&[
        "-u",
        &credential(login, password),
        "ftp://127.0.0.1/",
        "--list-only",
    ])
}

/// The bytes of the live configuration the daemon is started against.
///
/// Read from the path the daemon reads, never from a render held in memory.
fn live_configuration() -> String {
    std::fs::read_to_string(AgentPaths::VSFTPD_CONFIG_PATH)
        .expect("an enable must have left a configuration on disk")
}

/// Appends `line` to the live configuration, which is what an operator or a
/// second tool would do.
fn plant_in_the_live_configuration(line: &str) {
    let mut contents = live_configuration();
    // The rendered file ends WITHOUT a trailing newline, measured on this host:
    // appending straight onto it produces `require_ssl_reuse=NOforce_local_…`,
    // one unrecognised variable, and a vsftpd that exits 2 printing nothing at
    // all on the Debian family. That is a real hazard for anyone appending to
    // this file — it is reported as a finding — and it is not what this test is
    // about, so the newline is ensured here.
    if !contents.ends_with('\n') {
        contents.push('\n');
    }
    contents.push_str(line);
    contents.push('\n');
    std::fs::write(AgentPaths::VSFTPD_CONFIG_PATH, contents)
        .expect("the live configuration is writable by root");
}

/// Every `key=value` key the live configuration carries, in order, comments and
/// blank lines dropped.
fn live_configuration_keys() -> Vec<String> {
    live_configuration()
        .lines()
        .map(str::trim)
        .filter(|line| !line.is_empty() && !line.starts_with('#'))
        .filter_map(|line| line.split_once('=').map(|(key, _)| key.trim().to_owned()))
        .collect()
}

/// Creates a system login that is NOT in the FTPS group, with a real password.
///
/// # Panics
///
/// Panics when `useradd` or `chpasswd` refuses.
fn plant_a_non_member(login: &str, password: &str) {
    let distro = PolygonVsftpd::distro();
    let outcome = Command::new(distro.useradd_binary())
        .args(["--no-create-home", "--shell", distro.nologin_shell(), login])
        .output()
        .expect("the polygon image installs useradd");
    assert!(
        outcome.status.success(),
        "the non-member must be created: {}",
        String::from_utf8_lossy(&outcome.stderr)
    );

    set_a_password(login, password);
}

/// Sets `login`'s password through the host's own `chpasswd`.
///
/// Used for the two logins this suite plants rather than creates — a non-member
/// and `root` — so that the credential they are refused with is a REAL one. A
/// refusal of a login that had no password would prove nothing.
///
/// # Panics
///
/// Panics when `chpasswd` refuses.
fn set_a_password(login: &str, password: &str) {
    let mut child = Command::new(PolygonVsftpd::distro().chpasswd_binary())
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::null())
        .spawn()
        .expect("the polygon image installs chpasswd");

    {
        use std::io::Write as _;
        child
            .stdin
            .as_mut()
            .expect("stdin was piped")
            .write_all(format!("{login}:{password}\n").as_bytes())
            .expect("chpasswd reads its standard input");
    }

    let status = child.wait().expect("chpasswd must be waited for");
    assert!(
        status.success(),
        "chpasswd must accept the line for {login}"
    );
}

/// Removes a login this suite planted, reporting rather than panicking.
fn remove_planted_login(login: &str) {
    let outcome = Command::new(PolygonVsftpd::distro().userdel_binary())
        .arg(login)
        .output();
    if let Ok(outcome) = outcome
        && !outcome.status.success()
    {
        eprintln!(
            "the planted login {login} could not be removed: {}",
            String::from_utf8_lossy(&outcome.stderr)
        );
    }
}

/// The raw shadow password field the host holds for `login`.
///
/// # Panics
///
/// Panics when `getent` will not answer, or prints something with no field.
fn shadow_field(login: &str) -> String {
    let outcome = Command::new(PolygonVsftpd::distro().getent_binary())
        .args(["shadow", login])
        .output()
        .expect("the polygon image installs getent");
    assert!(
        outcome.status.success(),
        "getent must hold a shadow entry for {login}"
    );

    let line = String::from_utf8_lossy(&outcome.stdout);
    let mut fields = line.lines().next().unwrap_or_default().split(':');
    let name = fields.next().unwrap_or_default();
    assert_eq!(name, login, "getent must answer about the login asked for");

    fields.next().unwrap_or_default().to_owned()
}

/// Whether a shadow password field could authenticate a login.
///
/// The same reading `StoredPassword` makes: a lock marker in front of the field,
/// or no hash at all, is a login nothing can match. Written here rather than
/// reached for out of `ops`, because a test that asked the code under test what
/// its own field means would agree with it whatever it said.
fn can_authenticate(field: &str) -> bool {
    !field.is_empty() && !field.starts_with('!') && !field.starts_with('*')
}

/// The harness property every other test in this file rests on: a shadow
/// password can be verified inside THIS container at all.
///
/// It asks the same helper `pam_unix.so` asks, about an account that certainly
/// exists (`root`) with a password that certainly does not match, and requires
/// the answer to be a REFUSAL of that password rather than an inability to look
/// it up. A refusal proves the shadow database was read; PAM_AUTHINFO_UNAVAIL
/// proves it was not, and in that state every login in this suite is refused for
/// a reason that has nothing to do with the agent — which is exactly how this
/// suite spent a day looking like a product defect.
///
/// Why it can fail, so it is not decoration: `chmod 0000 /etc/shadow` in a
/// privileged container of the image puts the condition back, and this test then
/// prints the sentence below. That is the mutation it was written against.
///
/// The inverse control is the assertion itself: it demands a specific code (7)
/// and refuses an unknown one, so a helper that has moved, stopped running, or
/// started answering something else fails here rather than passing quietly.
#[test]
#[ignore = "asks this container's own PAM helper about the shadow database: polygon only"]
fn the_shadow_database_can_be_read_for_authentication_in_this_container() {
    PolygonVsftpd::require_polygon();

    let mut helper = Command::new(SHADOW_PASSWORD_HELPER)
        .args(["root", "nullok"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap_or_else(|error| {
            panic!(
                "{SHADOW_PASSWORD_HELPER} could not be started ({error}). It is the binary \
                 pam_unix.so verifies a shadow password through on both families, so this \
                 suite has measured nothing about authentication without it."
            )
        });
    helper
        .stdin
        .take()
        .expect("the helper's stdin was piped one line above")
        .write_all(A_PASSWORD_NO_ACCOUNT_HAS.as_bytes())
        .expect("the helper reads the offered password from stdin");
    let outcome = helper
        .wait_with_output()
        .expect("the helper was spawned and must be waitable");

    match outcome.status.code() {
        Some(PAM_AUTH_ERR) => {}
        Some(PAM_AUTHINFO_UNAVAIL) => panic!(
            "the shadow database cannot be read for authentication in this container: \
             {SHADOW_PASSWORD_HELPER} answered PAM_AUTHINFO_UNAVAIL ({PAM_AUTHINFO_UNAVAIL}) \
             instead of refusing a wrong password ({PAM_AUTH_ERR}), so EVERY login is refused \
             here whatever password it offers and no other test in this suite means anything. \
             MEASURED cause: the host's AppArmor profile /etc/apparmor.d/unix-chkpwd attaches \
             by path to {SHADOW_PASSWORD_HELPER} in a container that runs unconfined — which \
             `docker run --privileged` does — and withholds capability dac_override, while \
             this family ships /etc/shadow mode 0000 root:root, which root can read only with \
             that capability. docker/polygon/alma9.Dockerfile makes the file 0400 root:root so \
             its OWNER can read it; if this test is red, that step is missing from the image \
             this container was started from, or something has put the mode back."
        ),
        other => panic!(
            "{SHADOW_PASSWORD_HELPER} answered {other:?} for a login that exists and a password \
             that does not match it. This probe knows two answers — {PAM_AUTH_ERR} (read and \
             refused) and {PAM_AUTHINFO_UNAVAIL} (could not read) — and an unknown third is a \
             failure to observe, not an agreement: stderr was {}",
            String::from_utf8_lossy(&outcome.stderr).trim()
        ),
    }
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_login_reaches_its_own_home_over_tls_and_a_file_it_uploads_belongs_to_the_account() {
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsupload");
    let existing = give_the_account_a_file(&account);
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    // The customer's own file, seen through the bind mount inside the jail.
    let listing = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        listing.contains(LOGIN_SUCCESSFUL),
        "the password the agent handed chpasswd must be the one the daemon \
         accepts: {listing}\n{PAM_ON_THIS_IMAGE}"
    );
    let home_listing = curl(&[
        "--ssl-reqd",
        "-u",
        &credential(login.name(), CUSTOMER_PASSWORD),
        "ftp://127.0.0.1/home/",
        "--list-only",
    ]);
    assert!(
        home_listing.contains(CUSTOMER_FILE),
        "the account's real home must be what the session reaches: {home_listing}"
    );

    // The upload, and the identity behind it.
    let source = account.home().join("upload-source.txt");
    std::fs::write(&source, "uploaded by the customer\n").expect("root can write here");
    let upload = curl(&[
        "--ssl-reqd",
        "-u",
        &credential(login.name(), CUSTOMER_PASSWORD),
        "-T",
        source.to_str().expect("a utf-8 path"),
        "ftp://127.0.0.1/home/uploaded.txt",
    ]);
    assert!(
        !upload.contains("530") && !upload.contains("550"),
        "the upload must be accepted: {upload}"
    );

    let landed = account.home().join("uploaded.txt");
    let metadata = std::fs::metadata(&landed).unwrap_or_else(|error| {
        panic!("the upload must land in the account's real home at {landed:?}: {error}")
    });
    let ids = account.ids();
    assert_eq!(
        (metadata.uid(), metadata.gid()),
        (ids.uid(), ids.gid()),
        "a file the login uploads must belong to the ACCOUNT, which is what \
         `useradd --non-unique --uid` is for: a login with an identity of its own \
         would write files the account's own php-fpm pool cannot read"
    );

    assert!(
        existing.exists(),
        "nothing here may remove the customer's files"
    );
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_client_that_will_not_negotiate_tls_cannot_log_in() {
    // The owner's second decision, OBSERVED rather than read out of a config
    // file. Asserting that `force_local_logins_ssl=YES` appears somewhere would
    // pass on a daemon that never read that file, and on one that read an
    // appended `NO` after it.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsplain");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let refused = list_without_tls(login.name(), CUSTOMER_PASSWORD);

    assert!(
        refused.contains(ENCRYPTION_REQUIRED),
        "a plaintext login must be refused for THAT reason — a wrong password is \
         a 530 too, and the two are what this test tells apart: {refused}"
    );
    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "the credential must never cross the wire in the clear: {refused}"
    );

    // The inverse control. A daemon that refused every login would satisfy the
    // assertion above while proving nothing, so the SAME credential is shown to
    // work once TLS is negotiated.
    let accepted = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "the same credential must be accepted over TLS, or the refusal above is \
         about the password and not about the encryption: {accepted}\n{PAM_ON_THIS_IMAGE}"
    );
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_login_cannot_leave_its_jail() {
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsjail");
    let neighbour = PolygonAccount::create("ftpsneighbour");
    std::fs::write(
        neighbour.home().join("secret.txt"),
        "the neighbour's data\n",
    )
    .expect("root can write here");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    // What the session's own root actually holds: the bind mount, and nothing
    // else. Asserted as the WHOLE listing, because a chroot that had not taken
    // effect would show the host's root directory here.
    let entries = entries_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert_eq!(
        entries,
        vec![HOME_INSIDE_THE_JAIL.to_owned()],
        "the session's root must be the jail and hold only the bind mount, got {entries:?}"
    );

    // TWO slashes after the authority, which is how curl spells an ABSOLUTE ftp
    // path: `ftp://host/etc/` is `etc/` RELATIVE to the login directory, and a
    // relative path fails inside the jail whether or not the chroot is in force.
    // Measured: with `chroot_local_user=NO` planted in the template, the
    // single-slash form still failed and this test still passed — it was
    // decoration, and the mutation pass is what exposed it.
    for escape in ["ftp://127.0.0.1//etc/", "ftp://127.0.0.1//"] {
        let refused = curl(&[
            "--ssl-reqd",
            "-u",
            &credential(login.name(), CUSTOMER_PASSWORD),
            escape,
            "--list-only",
        ]);
        assert!(
            !refused.contains("passwd") && !refused.contains("var\n") && !refused.contains("usr"),
            "{escape} must not reach the host's own root filesystem: {refused}"
        );
    }

    let neighbours_home = format!(
        "ftp://127.0.0.1/{}/",
        neighbour.home().to_str().expect("a utf-8 path")
    );
    let refused = curl(&[
        "--ssl-reqd",
        "-u",
        &credential(login.name(), CUSTOMER_PASSWORD),
        &neighbours_home,
        "--list-only",
    ]);
    assert!(
        !refused.contains("secret.txt"),
        "a login must not reach another account's home: {refused}"
    );
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn an_account_that_is_not_in_the_ftps_group_cannot_authenticate_even_with_a_valid_password() {
    // The assertion the installer's own polygon check declared UNOBSERVED. The
    // PAM stack `installer/lib/89-ftps.sh` wrote requires membership of the FTPS
    // group, and that membership is the ENTIRE authorization for this daemon.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsgroup");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    plant_a_non_member("ftpsoutsider", CUSTOMER_PASSWORD);

    let refused = list_over_tls("ftpsoutsider", CUSTOMER_PASSWORD);
    remove_planted_login("ftpsoutsider");

    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "a login outside the FTPS group must be refused THOUGH ITS PASSWORD IS \
         CORRECT: {refused}"
    );
    assert!(refused.contains("530"), "{refused}");

    // The inverse control: the same daemon, the same TLS, the same password
    // alphabet — and a login that IS in the group is let in. Without this, a
    // daemon refusing everything would pass.
    let accepted = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "a member must be accepted, or the refusal above is not about the group: \
         {accepted}\n{PAM_ON_THIS_IMAGE}"
    );
}

#[test]
#[ignore = "gives root a password for the length of the test: polygon only"]
fn root_cannot_log_in_over_ftps_even_when_root_has_a_password() {
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsroot");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    // Given for the length of this test and taken away at the end of it. Root on
    // a polygon container has no password at all, and a refusal of a login that
    // could not authenticate anywhere would prove nothing.
    let before = shadow_field("root");
    assert!(
        !can_authenticate(&before),
        "root must start out unable to authenticate, or this test has nothing to \
         give it: {before:?}"
    );
    set_a_password("root", CUSTOMER_PASSWORD);
    assert!(
        can_authenticate(&shadow_field("root")),
        "the password must really have been set, or the refusal below is a \
         refusal of a credential that never existed"
    );

    let refused = list_over_tls("root", CUSTOMER_PASSWORD);

    // Taken away again before anything is asserted, so a failing assertion
    // cannot leave a usable password on the container's root account.
    let restore = Command::new(PolygonVsftpd::distro().usermod_binary())
        .args(["--lock", "root"])
        .output()
        .expect("the polygon image installs usermod");
    assert!(restore.status.success(), "root must be locked again");
    let after = shadow_field("root");
    assert!(
        !can_authenticate(&after),
        "root's shadow field must be back to something that cannot \
         authenticate, got {after:?}"
    );

    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "root must never reach this daemon, whatever password it holds: {refused}"
    );

    let accepted = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "the inverse control: a real FTPS login must still be accepted: {accepted}"
    );
}

#[test]
#[ignore = "edits the live config and restarts the daemon: polygon only"]
fn the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen() {
    // vsftpd takes the LAST occurrence of a key, so one appended
    // `force_local_logins_ssl=NO` turns forced TLS off on a configuration that
    // still parses, still starts and still greets with 220 — and NEITHER of the
    // enable path's two validation layers can see it: one runs before the swap,
    // the other asks liveness questions the sabotaged file answers perfectly.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsdup");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (mut daemon, _) = PolygonVsftpd::apply(&configuration());

    // Part 1: the rendered file carries every key once.
    let keys = live_configuration_keys();
    assert!(
        keys.len() >= 25,
        "an empty or truncated key set would pass the duplicate check loudest; \
         the rendered configuration carries far more than this, got {}",
        keys.len()
    );
    let mut sorted = keys.clone();
    sorted.sort();
    let before = sorted.len();
    sorted.dedup();
    assert_eq!(
        sorted.len(),
        before,
        "the rendered configuration must carry each key exactly once, got {keys:?}"
    );

    // Part 2: THE POSITIVE CONTROL. Plant the duplicate, restart, and watch both
    // the check above fail by name AND the plaintext refusal stop happening. A
    // duplicate check nobody has seen fail is a green gate that certifies
    // nothing.
    assert!(
        list_without_tls(login.name(), CUSTOMER_PASSWORD).contains(ENCRYPTION_REQUIRED),
        "the plaintext login must be refused before the sabotage, or part 2 \
         measures nothing"
    );
    plant_in_the_live_configuration("force_local_logins_ssl=NO");
    daemon.restart();

    let mut sabotaged = live_configuration_keys();
    let planted = sabotaged.len();
    sabotaged.sort();
    sabotaged.dedup();
    assert!(
        sabotaged.len() < planted,
        "the duplicate check must SEE the planted key, or it is decoration"
    );
    let with_tls_off = list_without_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        !with_tls_off.contains(ENCRYPTION_REQUIRED),
        "one appended line must be enough to switch forced TLS off on a real \
         daemon — that is the risk this whole check exists for, and it is \
         measured on this host rather than quoted: {with_tls_off}"
    );

    // Part 3: the read-back's own half. Plant the IPv4-only listen pair AFTER
    // the rendered dual-stack one and ask the agent what mode is in force. A
    // read that took the FIRST occurrence would answer with what the panel
    // rendered and agree with itself forever.
    plant_in_the_live_configuration("listen_ipv6=NO\nlisten=YES");
    let state = get_ftps_status(
        &ProcessFtpsHost::new(),
        PolygonVsftpd::distro(),
        Some(&hostname()),
    )
    .expect("the status must be readable");
    assert_eq!(
        state.listen_mode,
        Some(maran_ops::ftps::ListenMode::Ipv4Only),
        "the status must report the pair the DAEMON obeys — the last one — and \
         not the pair the panel rendered"
    );
    assert_eq!(
        state.forced_tls,
        Some(false),
        "and the same read must see the forced-TLS key that was switched off"
    );

    // Part 4: the repair is the constraint demonstrating itself. `safe_write`
    // replaces the file WHOLE, so re-running the enable is the whole fix.
    daemon
        .reapply(&configuration())
        .expect("the re-enable must succeed against a daemon that was answering");

    let keys = live_configuration_keys();
    let mut sorted = keys.clone();
    sorted.sort();
    let before = sorted.len();
    sorted.dedup();
    assert_eq!(
        sorted.len(),
        before,
        "the repair must leave no duplicate: {keys:?}"
    );
    assert!(
        list_without_tls(login.name(), CUSTOMER_PASSWORD).contains(ENCRYPTION_REQUIRED),
        "and the plaintext login must be refused again"
    );
    let state = get_ftps_status(
        &ProcessFtpsHost::new(),
        PolygonVsftpd::distro(),
        Some(&hostname()),
    )
    .expect("the status must be readable");
    assert_eq!(
        state.listen_mode,
        Some(maran_ops::ftps::ListenMode::DualStack)
    );
    assert_eq!(state.forced_tls, Some(true));
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn suspending_the_account_refuses_its_ftps_login_and_resuming_gives_it_back() {
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpssuspend");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    assert!(
        list_over_tls(login.name(), CUSTOMER_PASSWORD).contains(LOGIN_SUCCESSFUL),
        "the login must work before the suspension, or its refusal afterwards \
         proves nothing\n{PAM_ON_THIS_IMAGE}"
    );

    let logins_host = ProcessLoginsHost::new();
    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), true)
        .expect("the suspension must be applied");

    let refused = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "a suspended account's FTPS login must be refused by the daemon — this is \
         the credential `usermod --lock <account>` does not reach: {refused}"
    );

    // The attestation the panel reads, on the same host, in the same breath: it
    // must name the DAEMON behind the login it saw and report what it could not
    // speak for.
    let operations = AccountOperations::new(
        ProcessSystemHost::new(PolygonVsftpd::distro()),
        PolygonVsftpd::distro(),
    );
    let state = operations
        .suspension_state(
            &ProcessSiteHost::new(),
            &ProcessCronHost::new(PolygonVsftpd::distro()),
            &logins_host,
            account.name(),
        )
        .expect("the suspension state must be readable");
    let fact = state
        .logins
        .logins
        .iter()
        .find(|held| held.name == login.name())
        .unwrap_or_else(|| panic!("the attestation must see the login: {:?}", state.logins));
    assert_eq!(
        fact.protocol,
        LoginProtocol::Ftps,
        "the attestation must say which daemon serves the credential it turned"
    );
    assert!(fact.locked, "and that it is locked");
    assert_eq!(
        state.logins.unmanaged, 0,
        "this account holds nothing outside the two jails, and the attestation \
         must say so with a number rather than by staying silent"
    );

    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), false)
        .expect("the resume must be applied");

    let accepted = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "resuming must give the customer their own credential back unchanged: \
         {accepted}"
    );
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn changing_a_suspended_logins_password_leaves_it_refused_until_the_account_is_resumed() {
    // The defect `sftp::set_sftp_password` was repaired for, asked of the FTPS
    // daemon: `chpasswd` REPLACES the shadow field, so without the re-assert a
    // suspended customer changing their password would unlock their own login
    // while the panel went on reporting them suspended.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsrotate");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let logins_host = ProcessLoginsHost::new();
    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), true)
        .expect("the suspension must be applied");
    assert!(
        shadow_field(login.name()).starts_with('!'),
        "the suspension must really be a marked shadow field"
    );

    set_ftps_password(
        &ProcessFtpsHost::new(),
        PolygonVsftpd::distro(),
        account.name(),
        login.user(),
        &Password::parse(ROTATED_PASSWORD).expect("a valid password"),
    )
    .expect("the password change must succeed");

    // The artefact, not the return value: an rpc that returned Ok having done
    // nothing would pass an assertion about its result.
    assert!(
        shadow_field(login.name()).starts_with('!'),
        "the shadow field must still carry the suspension marker after the write"
    );
    let refused = list_over_tls(login.name(), ROTATED_PASSWORD);
    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "a suspended customer must not be able to unlock their own login by \
         changing its password: {refused}"
    );
    let old = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        !old.contains(LOGIN_SUCCESSFUL),
        "and not with the old one either: {old}"
    );

    // And the promise the restore makes: the password the customer chose is the
    // password the login has once the panel lifts the suspension.
    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), false)
        .expect("the resume must be applied");

    let accepted = list_over_tls(login.name(), ROTATED_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "the rotated password must be the one that works after the resume: {accepted}"
    );
    let stale = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        !stale.contains(LOGIN_SUCCESSFUL),
        "and the password it replaced must not: {stale}"
    );
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_password_change_authorised_for_one_account_cannot_reach_another_accounts_login() {
    // The cross-tenant hole the SFTP repair closed alongside the suspension one.
    // `<account>_<name>` has no unique decomposition when account names may carry
    // the separator, so the ownership check is a JAIL comparison and not a name
    // parse.
    PolygonVsftpd::require_polygon();
    let attacker = PolygonAccount::create("ftpsa");
    let victim = PolygonAccount::create("ftpsa_files");
    PolygonVsftpd::place_certificate_material(&hostname());

    // The collision: the attacker asking for the suffix `files` addresses the
    // string `ftpsa_files`, which is the VICTIM ACCOUNT's own system user. No
    // decode of that name can tell the two readings apart.
    let stolen = FtpsUserName::for_account(attacker.name(), LOGIN_SUFFIX).expect("a valid name");
    assert_eq!(
        stolen.as_str(),
        victim.name().as_str(),
        "the collision this test is about must actually exist on this host"
    );

    let attackers_login =
        PolygonFtpsLogin::create_with_suffix(attacker.name(), "web", CUSTOMER_PASSWORD);
    let victims_login = PolygonFtpsLogin::create(victim.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let before = shadow_field(victim.name().as_str());

    let result = set_ftps_password(
        &ProcessFtpsHost::new(),
        PolygonVsftpd::distro(),
        attacker.name(),
        &stolen,
        &Password::parse(ROTATED_PASSWORD).expect("a valid password"),
    );

    assert!(
        matches!(result, Err(FtpsError::NotFound)),
        "a login the account does not hold must be refused: {result:?}"
    );
    assert_eq!(
        shadow_field(victim.name().as_str()),
        before,
        "and the other tenant's credential must be byte-for-byte what it was"
    );

    // The inverse control: the attacker's OWN login IS reachable, so the refusal
    // above is about ownership and not about the operation refusing everything.
    set_ftps_password(
        &ProcessFtpsHost::new(),
        PolygonVsftpd::distro(),
        attacker.name(),
        attackers_login.user(),
        &Password::parse(ROTATED_PASSWORD).expect("a valid password"),
    )
    .expect("an account's own login must be re-credentialled");
    assert!(
        list_over_tls(attackers_login.name(), ROTATED_PASSWORD).contains(LOGIN_SUCCESSFUL),
        "the rotation must really have taken effect"
    );
    assert!(
        list_over_tls(victims_login.name(), CUSTOMER_PASSWORD).contains(LOGIN_SUCCESSFUL),
        "and the victim's own login must still work with its own password"
    );

    PolygonVsftpd::remove_certificate_material(&hostname());
}

#[test]
#[ignore = "holds two real FTPS sessions open across a real suspension: polygon only"]
fn an_authenticated_session_is_ended_when_the_account_is_suspended() {
    // THIS TEST WAS RE-PINNED, DELIBERATELY, AND ITS PREVIOUS NAME SAID THE
    // OPPOSITE: `an_authenticated_session_keeps_transferring_after_the_account_
    // is_suspended`. That test measured today's behaviour and said in its own
    // message what a red line would mean — "the agent culls the account's
    // processes ... re-pin this test to the NEW behaviour instead of deleting
    // it". This is that re-pin. The owner asked for option 3 of
    // `docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md`
    // § "The options, their costs, and a recommendation"; the cull is argued in
    // `docs/superpowers/notes/2026-09-12-suspension-session-cull-threat-note.md`.
    //
    // The FTPS half matters on its own and not as a copy of the SFTP one: this is
    // a different daemon with a different authentication model, and the process
    // the cull has to reach is the one vsftpd drops to AFTER it chroots. A change
    // that ended SFTP sessions and left FTPS ones running would be a promise kept
    // by half, which is the shape this pair of tests exists to refuse.
    //
    // `idle_session_timeout=600` is NOT removed by this change and is still
    // pinned by its own test: it bounds the idle sessions of every account that
    // is not suspended, which is every account this cull never touches.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpslive");
    give_the_account_a_file(&account);
    let neighbour = PolygonAccount::create("ftpsnear");
    give_the_account_a_file(&neighbour);
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let neighbour_login = PolygonFtpsLogin::create(neighbour.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let (mut session, greeting) = FtpsControlSession::login(login.name(), CUSTOMER_PASSWORD);
    assert!(
        greeting.contains(LOGIN_SUCCESSFUL),
        "the session must be authenticated BEFORE the suspension, or nothing \
         below is about a session that outlived one: {greeting}\n{PAM_ON_THIS_IMAGE}"
    );
    let (mut neighbour_session, neighbour_greeting) =
        FtpsControlSession::login(neighbour_login.name(), CUSTOMER_PASSWORD);
    assert!(
        neighbour_greeting.contains(LOGIN_SUCCESSFUL),
        "the neighbour's session must be authenticated too, or it cannot survive \
         anything: {neighbour_greeting}"
    );
    let before = session.retrieve(&format!("/{HOME_INSIDE_THE_JAIL}/{CUSTOMER_FILE}"));
    assert_eq!(
        before.bytes,
        CUSTOMER_CONTENT,
        "the open session must be able to transfer before the suspension, or a \
         failure afterwards would be about this client and not about the \
         suspension: {before:?}",
        before = (&before.began, &before.bytes, &before.finished)
    );

    let logins_host = ProcessLoginsHost::new();
    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), true)
        .expect("the suspension must be applied");

    // The control that makes the rest of this test mean something: the
    // suspension REALLY landed on this host at this moment. Without it, a
    // session that stopped working would be evidence of nothing.
    assert!(
        shadow_field(login.name()).starts_with('!'),
        "the suspension must have marked the shadow field before this test can \
         claim anything about the session behind it"
    );
    let new_session_is_refused = list_over_tls(login.name(), CUSTOMER_PASSWORD);
    assert!(
        !new_session_is_refused.contains(LOGIN_SUCCESSFUL),
        "a NEW session with the same credential must be refused while the \
         account is suspended: {new_session_is_refused}"
    );

    // THE FINDING. vsftpd serves a login from a process that has dropped to the
    // account's uid, so the cull reaches it: the control connection is gone and
    // the daemon cannot answer a `PWD` it never receives.
    // `probe` and not `command`: the cull kills the process on the other end of
    // this client's pipe, so the WRITE is what fails, and `command` panics on a
    // failed write by design. `probe` reports that as `SESSION_CLOSED`, which is
    // the measurement rather than a harness fault.
    let still_answered = session.probe("PWD");
    assert!(
        !still_answered.starts_with("257"),
        "MEASURED: suspending an account ends the FTPS session it already had \
         open. THIS LINE GOING RED means a suspended customer's live session \
         survived the cull — check that `end_account_sessions` ran, that it named \
         this account's uid, and that `pkill` is installed on this image: \
         {still_answered}"
    );

    // The cross-tenant inverse control, and the daemon's own liveness proof: a
    // session belonging to a DIFFERENT account is untouched and still moves
    // bytes, at the same moment, through the same daemon.
    let neighbour_after =
        neighbour_session.retrieve(&format!("/{HOME_INSIDE_THE_JAIL}/{CUSTOMER_FILE}"));
    assert_eq!(
        neighbour_after.bytes,
        CUSTOMER_CONTENT,
        "a cull that reached another account's uid would be a cross-tenant \
         denial of service, and a daemon that had stopped serving every session \
         would make the assertion above vacuous: {neighbour_after:?}",
        neighbour_after = (
            &neighbour_after.began,
            &neighbour_after.bytes,
            &neighbour_after.finished
        )
    );

    set_account_logins_locked(&logins_host, PolygonVsftpd::distro(), account.name(), false)
        .expect("the resume must be applied");
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_deleted_accounts_ftps_credential_is_refused_by_the_daemon_after_the_name_is_recycled() {
    // `account_deletion_on_a_real_host.rs` proves the cascade on the MACHINE —
    // the passwd entry is gone, the bind mount is down, the unit file is gone —
    // and says in as many words that the protocol-level login belongs to the
    // FTPS polygon work. This is that half: the daemon itself, asked with the
    // dead credential, in a real session, after the account name has been
    // recycled the way a hosting panel recycles one.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsrecycle");
    PolygonVsftpd::place_certificate_material(&hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let name = account.name().clone();
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    assert!(
        list_over_tls(login.name(), CUSTOMER_PASSWORD).contains(LOGIN_SUCCESSFUL),
        "the credential must work before the deletion, or its refusal afterwards \
         proves nothing\n{PAM_ON_THIS_IMAGE}"
    );
    let dead_login = login.name().to_owned();

    // The FTPS half of the cascade, and then the account itself — the same two
    // steps, in the same order, the account deletion performs.
    drop(login);
    drop(account);

    // Recycled: a new customer, the same name, the same uid space.
    let successor = PolygonAccount::create(name.as_str());
    let new_login = PolygonFtpsLogin::create(successor.name(), ROTATED_PASSWORD);
    assert_eq!(
        new_login.name(),
        dead_login.as_str(),
        "the recycled account's login must be the SAME STRING as the dead one, or \
         this test is asking the daemon about a name nobody would collide with"
    );

    let refused = list_over_tls(&dead_login, CUSTOMER_PASSWORD);
    assert!(
        !refused.contains(LOGIN_SUCCESSFUL),
        "the previous tenant's password must be refused by the daemon after the \
         account was deleted and the name re-created: {refused}"
    );

    // The inverse control. A daemon refusing everything — a missing certificate,
    // a jail that was never filled, a group the successor never joined — would
    // satisfy the refusal above while proving nothing about the credential.
    let accepted = list_over_tls(new_login.name(), ROTATED_PASSWORD);
    assert!(
        accepted.contains(LOGIN_SUCCESSFUL),
        "the successor's own credential must be accepted, or the refusal above is \
         about the daemon and not about the dead password: {accepted}\n{PAM_ON_THIS_IMAGE}"
    );

    PolygonVsftpd::remove_certificate_material(&hostname());
}

/// The fragment curl prints when a chain it was asked to verify does not build.
///
/// Measured on both polygon families. Asserted as this sentence rather than as a
/// bare non-zero exit, because every refusal in this suite is a non-zero exit and
/// the ones this test has to tell apart are "I do not trust the certificate" and
/// "I could not log in".
const CERTIFICATE_REFUSED: &str = "SSL certificate problem";

/// Where a name is made to resolve, so a client can ask for the daemon by the
/// name the certificate covers.
const HOSTS_FILE: &str = "/etc/hosts";

/// Makes [`HOSTNAME`] resolve to loopback, so a verifying client can be given a
/// URL carrying the name the certificate is issued for.
///
/// Chain verification is name verification too: a client handed `ftp://127.0.0.1/`
/// would reject material whose only name is `ftps.polygon.test` for a reason that
/// has nothing to do with the chain, and a test that then passed `-k` to make it
/// work would be the vacuity this whole file is about. So the name is made real
/// rather than the check made loose.
///
/// Idempotent, and appends rather than rewrites: the container's `/etc/hosts` is
/// a bind mount carrying its own identity, and replacing it would break
/// everything else in the image.
///
/// # Panics
///
/// Panics when the file cannot be read or appended to.
fn make_the_hostname_resolve() {
    let contents = std::fs::read_to_string(HOSTS_FILE)
        .unwrap_or_else(|error| panic!("{HOSTS_FILE} must be readable: {error}"));
    if contents.split_whitespace().any(|field| field == HOSTNAME) {
        return;
    }

    let mut file = std::fs::OpenOptions::new()
        .append(true)
        .open(HOSTS_FILE)
        .unwrap_or_else(|error| panic!("{HOSTS_FILE} must be appendable: {error}"));
    writeln!(file, "127.0.0.1 {HOSTNAME}")
        .unwrap_or_else(|error| panic!("{HOSTS_FILE} must be writable: {error}"));
}

/// Runs curl with certificate verification ON, against `root` and nothing else.
///
/// The difference from [`curl`] is one flag and it is the whole point: there is
/// no `-k` here, and `--cacert` REPLACES the host's trust store rather than
/// adding to it, so the only authority this client will accept is the one the
/// test controls. A client that accepted the daemon for any other reason — a
/// root that leaked into the image, verification quietly switched off — is
/// therefore not something this function can produce a pass from, because the
/// inverse control below hands it a different root and requires a refusal.
///
/// `-v` for the same reason [`curl`] needs it: the assertions are about the
/// server's own replies, which only the decrypted control channel carries.
fn curl_verifying(root: &std::path::Path, arguments: &[&str]) -> String {
    let outcome = Command::new("/usr/bin/curl")
        .args(["-sS", "-v", "--connect-timeout", "10", "--max-time", "60"])
        .arg("--cacert")
        .arg(root)
        .args(arguments)
        .output()
        .expect("the polygon image installs curl");

    format!(
        "{}{}",
        String::from_utf8_lossy(&outcome.stdout),
        String::from_utf8_lossy(&outcome.stderr)
    )
}

/// The URL a verifying client is given: the daemon, by the name in its
/// certificate.
fn hostname_url() -> String {
    format!("ftp://{HOSTNAME}/")
}

/// The leaf certificate the daemon actually presents on the control port.
///
/// Asked with `openssl s_client -starttls ftp`, which performs the real `AUTH
/// TLS` exchange and needs no credential, so what comes back is the material the
/// RUNNING daemon serves and not a file read from anywhere.
///
/// # Panics
///
/// Panics when openssl cannot be run, or when its output carries no certificate
/// — which is a failure to observe and never an empty answer two sides of a
/// comparison could agree on.
fn certificate_the_daemon_serves() -> String {
    let outcome = Command::new(PolygonVsftpd::distro().openssl_binary())
        .args([
            "s_client",
            "-starttls",
            "ftp",
            "-connect",
            "127.0.0.1:21",
            "-showcerts",
        ])
        .stdin(Stdio::null())
        .output()
        .expect("the polygon image installs openssl");

    let printed = format!(
        "{}{}",
        String::from_utf8_lossy(&outcome.stdout),
        String::from_utf8_lossy(&outcome.stderr)
    );

    first_certificate_block(&printed).unwrap_or_else(|| {
        panic!(
            "openssl must print the certificate the daemon served, or this \
             comparison has nothing to compare: {printed}"
        )
    })
}

/// The first PEM certificate block in `pem`, delimiters included, with blank
/// lines and indentation removed.
///
/// Needed on both sides of the "does the daemon serve what the panel installed"
/// comparison: the store's `fullchain.pem` holds the leaf followed by its issuer,
/// and what a client is handed first is the leaf, so comparing whole texts would
/// compare a chain against a certificate and could never agree.
///
/// `None` when there is no complete block. The callers turn that into a failure
/// rather than into an equal pair of absences, which is the shape that would make
/// this comparison pass while observing nothing.
fn first_certificate_block(pem: &str) -> Option<String> {
    const BEGIN: &str = "-----BEGIN CERTIFICATE-----";
    const END: &str = "-----END CERTIFICATE-----";

    let start = pem.find(BEGIN)?;
    let end = pem[start..].find(END)? + start + END.len();

    Some(
        pem[start..end]
            .lines()
            .map(str::trim)
            .filter(|line| !line.is_empty())
            .collect::<Vec<_>>()
            .join("\n"),
    )
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn a_client_that_verifies_the_chain_against_the_panels_root_is_accepted_and_a_wrong_root_is_not() {
    // What the rest of this suite cannot say. Every other client here passes
    // `-k`, so the twelve assertions around this one are about ENCRYPTION: TLS
    // was negotiated, a credential crossed it, a file came back. None of them
    // would move if the daemon served expired material, material for another
    // name, or material signed by nobody — because the client was told not to
    // look. This test is the one that looks.
    //
    // It needs a root it controls, and the polygon can give it one: the material
    // the daemon serves is whatever `ops::ssl` left at the store's paths, and
    // this process is root in that store. So a throwaway authority issues a leaf
    // for the suite's hostname INTO those paths, and the client is given that
    // authority and no other.
    //
    // UNOBSERVED HERE: that a PUBLIC client trusts a real server. That is a fact
    // about other people's trust stores and about a certificate no isolated
    // container can obtain, and nothing on this host can observe it. What is
    // observed is the mechanism a public certificate would also travel: a chain
    // that builds to a known root, covering the name asked for, served by the
    // daemon on the configuration the agent rendered.
    PolygonVsftpd::require_polygon();
    make_the_hostname_resolve();
    let account = PolygonAccount::create("ftpstrust");
    let authority = PolygonCertificateAuthority::create(PolygonVsftpd::distro(), "ftps-trust-ca");
    authority.issue_into_the_panel_store(PolygonVsftpd::distro(), &hostname());
    let login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let verified = curl_verifying(
        &authority.root_path(),
        &[
            "--ssl-reqd",
            "-u",
            &credential(login.name(), CUSTOMER_PASSWORD),
            &hostname_url(),
            "--list-only",
        ],
    );
    assert!(
        !verified.contains(CERTIFICATE_REFUSED),
        "a client validating the chain against the authority that issued the \
         panel's material must not refuse the certificate: {verified}"
    );
    assert!(
        verified.contains(LOGIN_SUCCESSFUL),
        "the session must complete with verification ON, which is the whole claim \
         this test makes: {verified}\n{PAM_ON_THIS_IMAGE}"
    );

    // THE INVERSE CONTROL, and the reason this test is one test and not two. A
    // client with verification silently switched off would satisfy every
    // assertion above — that is exactly the regression this file is guarding —
    // and the only thing that can tell the two apart is the same client, the same
    // daemon, the same credential, and a root that did not sign this material. It
    // must REFUSE, and it must refuse for the certificate rather than for the
    // password.
    let unrelated =
        PolygonCertificateAuthority::create(PolygonVsftpd::distro(), "ftps-unrelated-ca");
    let rejected = curl_verifying(
        &unrelated.root_path(),
        &[
            "--ssl-reqd",
            "-u",
            &credential(login.name(), CUSTOMER_PASSWORD),
            &hostname_url(),
            "--list-only",
        ],
    );
    assert!(
        rejected.contains(CERTIFICATE_REFUSED),
        "a client given a root that signed nothing here must refuse the chain — \
         if this passes, `curl_verifying` is not verifying and the assertions \
         above mean only that something connected: {rejected}"
    );
    assert!(
        !rejected.contains(LOGIN_SUCCESSFUL),
        "a refused chain must end the session before any credential is accepted: \
         {rejected}"
    );

    PolygonVsftpd::remove_certificate_material(&hostname());
}

#[test]
#[ignore = "starts a real vsftpd and logs into it: polygon only"]
fn the_daemon_serves_the_certificate_the_panel_installed_and_not_some_other_file() {
    // The regression this branch has already found the shape of: the store's path
    // is spelled in the agent's render, in the unit, and in the installer, with
    // nothing comparing them — so an agent could render one path while the daemon
    // is started against another, and every encryption assertion in this suite
    // would still pass, because a certificate is a certificate.
    //
    // What makes this observable is that the two certificates can be made
    // DIFFERENT: the material at the store's paths is issued here, by this test's
    // own authority, so it is not the image's, not the previous test's, and not
    // anything a fallback could produce. Then the question "is the daemon serving
    // this file" has a wrong answer available to it.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsserved");
    let authority = PolygonCertificateAuthority::create(PolygonVsftpd::distro(), "ftps-served-ca");
    authority.issue_into_the_panel_store(PolygonVsftpd::distro(), &hostname());
    let _login = PolygonFtpsLogin::create(account.name(), CUSTOMER_PASSWORD);
    let (_daemon, _) = PolygonVsftpd::apply(&configuration());

    let store = SiteCertificate::for_domain(&hostname());
    let installed_chain =
        std::fs::read_to_string(store.certificate_path()).unwrap_or_else(|error| {
            panic!(
                "the store must hold the material this test installed at {:?}: {error}",
                store.certificate_path()
            )
        });
    let installed = first_certificate_block(&installed_chain).unwrap_or_else(|| {
        panic!("the installed chain must carry a certificate: {installed_chain}")
    });

    assert_eq!(
        certificate_the_daemon_serves(),
        installed,
        "the running daemon must present the leaf from {:?} — the file the panel's \
         own store keeps for this hostname — and not material from any other \
         path. A mismatch means the configuration the daemon read names a \
         certificate nobody in the panel installed, which no assertion about \
         encryption in this file can see.",
        store.certificate_path()
    );

    // The vacuity guard, on the axis that can go blind: the comparison above is
    // an equality, and an equality between two copies of the same accident proves
    // nothing. The leaf must actually be this test's own — issued by an authority
    // created seconds ago into a directory the test emptied — so it must NOT be
    // the self-signed placeholder the rest of the suite plants, whose chain is a
    // single certificate. This one is leaf-then-issuer.
    assert_ne!(
        installed_chain.trim(),
        installed,
        "the installed material must be a CHAIN and not a lone self-signed leaf, \
         or this test is comparing the daemon's certificate against the same \
         single certificate every other test in this file already serves"
    );

    PolygonVsftpd::remove_certificate_material(&hostname());
}

/// The value the live configuration gives `key`, or `None` when it names it nowhere.
///
/// The LAST occurrence wins, because that is the one vsftpd obeys — the same
/// reading `the_live_config_carries_each_key_exactly_once_and_a_planted_duplicate_is_seen`
/// exists to defend. `None` and `Some("0")` are deliberately different answers:
/// a key the configuration does not carry leaves the daemon on its own built-in
/// default, which is NOT the same fact as a bound this product set to zero.
fn live_configuration_value(key: &str) -> Option<String> {
    live_configuration()
        .lines()
        .map(str::trim)
        .filter(|line| !line.starts_with('#'))
        .filter_map(|line| line.split_once('='))
        .filter(|(name, _)| name.trim() == key)
        .map(|(_, value)| value.trim().to_owned())
        .next_back()
}

#[test]
#[ignore = "renders the live configuration the daemon is started against: polygon only"]
fn the_vsftpd_configuration_this_product_writes_bounds_an_idle_ftps_session_at_ten_minutes() {
    // THE OTHER HALF OF AN ASYMMETRY, PINNED SO IT CANNOT MOVE UNSEEN.
    //
    // `an_authenticated_session_is_ended_when_the_account_is_suspended` above and
    // its SFTP counterpart now measure that a SUSPENDED account's open session is
    // culled, and on that the two protocols agree. What they say nothing about is
    // a session nobody suspended: an ordinary customer who walked away from a
    // client. They do NOT agree on how long such a session can go on, and until
    // this case and
    // `sftp_on_a_real_host.rs::the_sshd_configuration_this_product_writes_puts_no_idle_bound_on_an_sftp_session`
    // existed, nothing observed the difference: this product SETS a ten-minute
    // idle bound for FTPS, and sets nothing at all for SFTP.
    //
    // Ten minutes is a value this repository chose and renders, so it is a bound
    // the product guarantees — unlike the SFTP side, where the absence is
    // whatever OpenSSH defaults to. That difference is the reason the two cases
    // are worded differently, and it is why the value is read from the file the
    // daemon is started against rather than from the template or from a golden.
    // A golden is compared byte-for-byte and is regenerated by the change that
    // alters it; this line goes red instead.
    PolygonVsftpd::require_polygon();
    let account = PolygonAccount::create("ftpsidle");
    PolygonVsftpd::place_certificate_material(&hostname());
    let (mut daemon, _) = PolygonVsftpd::apply(&configuration());

    // THE VACUITY GUARD, on the axis that can go blind: the key's ABSENCE would
    // leave vsftpd on its own default and must never read as a measurement of
    // this product's bound.
    let idle = live_configuration_value("idle_session_timeout").unwrap_or_else(|| {
        panic!(
            "the rendered configuration must NAME the idle bound; a missing key \
             leaves the daemon on its own built-in default and is not a bound \
             this product guarantees:\n{}",
            live_configuration()
        )
    });

    // THE FINDING.
    assert_eq!(
        idle, "600",
        "MEASURED: the configuration this product renders bounds an idle FTPS \
         session at ten minutes, in the file the daemon is started against. This \
         is the bound an already-open session outlives a suspension by, and SFTP \
         has no counterpart to it. If this line is red the bound has CHANGED, and \
         that is a decision rather than a defect: the value lives in \
         `agent/crates/templates/templates/vsftpd/vsftpd.conf.j2`, a LOWER bound \
         is felt by every customer and not only suspended ones, and `0` removes \
         the only bound this product states. The costed options are in \
         docs/superpowers/notes/2026-09-09-sftp-password-suspension-threat-note.md"
    );
    let data = live_configuration_value("data_connection_timeout").unwrap_or_default();
    assert_eq!(
        data, "120",
        "and the transfer half is recorded beside it, because a reader asking how \
         long a suspended customer keeps moving bytes needs both numbers"
    );

    // THE INVERSE CONTROL. A reader that answers `600` whatever the file says has
    // proved nothing. vsftpd obeys the LAST occurrence, so an appended line is
    // what an operator or a second tool would really do to this file, and the
    // same reader must SEE it.
    plant_in_the_live_configuration("idle_session_timeout=0");
    assert_eq!(
        live_configuration_value("idle_session_timeout").as_deref(),
        Some("0"),
        "the inverse control: this reader must see a CHANGED bound, and must see \
         the occurrence the daemon obeys rather than the one the panel rendered"
    );

    // The repair is `safe_write` replacing the file whole, which is the same
    // thing re-running the enable does on a server.
    daemon
        .reapply(&configuration())
        .expect("the re-enable must succeed against a daemon that was answering");
    assert_eq!(
        live_configuration_value("idle_session_timeout").as_deref(),
        Some("600"),
        "and the repair must put the product's own bound back"
    );

    drop(account);
}
