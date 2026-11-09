//! An account's login shell and open-files limit, judged on a real host
//! (issue #194).
//!
//! Every unit test of these features can show only that the agent DECIDED to pass
//! a shell to `useradd` or to write a file; what nothing else can show is what
//! the host then does with it. So each claim here is judged by looking at the
//! host afterwards - the password database for the shell, a real `su` session for
//! the limit - and never by the returned value alone.
//!
//! The claims, each paired with a control so a check that passes for everything
//! cannot pass:
//!
//! - An account created with the bash login has the distribution's bash in its
//!   password entry, and one created without a choice has the nologin shell.
//! - Suspension takes the shell away, and unsuspension gives back the shell the
//!   caller states: bash for an account that had it, nologin for one that did not.
//! - An open-files limit written for an account is the limit a real PAM session
//!   of that account starts with, and removing it returns the session to what it
//!   started with.
//!
//! **Unobserved here, stated rather than implied:**
//!
//! - The INODE quota. The polygon images stand in for `setquota` with a script
//!   that accepts and does nothing (`docker/polygon/stand-ins/setquota-stand-in.sh`),
//!   because a container's overlay filesystem holds no quota; the inode figures
//!   are pinned by the `ops::accounts` unit tests on the argv, and their effect
//!   needs the loop-mounted harness of `docker/polygon/asserts/quota-harness.sh`.
//! - The open-files limit of a php-fpm worker or a cron job. `pam_limits` does not
//!   reach them, and this suite does not claim it does; it asks `su`'s PAM stack
//!   only. Whether `sshd`'s stack loads `pam_limits` on each family is a separate
//!   measurement that needs a running sshd.
//! - The removal of the drop-in by an account's DELETION, which needs the whole
//!   cascade's database server; the ordering is pinned by the `ops::accounts`
//!   unit tests against a recording host.
//!
//! These tests need to run as root inside a polygon container, because they
//! create real system users and write under `/etc/security/limits.d`.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::os::unix::fs::PermissionsExt as _;
use std::path::Path;
use std::process::Command;

use maran_agent_core::agent_paths::AgentPaths;
use maran_agent_core::utils::free_space_floor::FreeSpaceFloor;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::open_files_limit::OpenFilesLimit;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{
    AccountOperations, LoginShell, NewAccountSettings, ProcessSystemHost, ResourceCeiling,
    set_account_open_files_limit,
};

/// The environment variable each polygon image sets, naming itself.
const POLYGON_MARKER: &str = "MARAN_POLYGON";

/// The limit the session case writes. Distinct from the soft limit a fresh root
/// session carries (1024 on a stock image, much larger inside a container), so a
/// session that never read the file cannot report it by accident.
const WRITTEN_LIMIT: u32 = 2048;

/// One real account made by `AccountOperations::create` with the settings a case
/// states, and removed when the case ends, passed or panicked.
struct LimitsAccount {
    /// The validated name.
    name: AccountName,
}

impl LimitsAccount {
    /// Creates the account with `settings`, or fails the case saying why not.
    ///
    /// # Panics
    ///
    /// Panics outside a polygon container, when `useradd` refuses, or when the
    /// account's shell field cannot be read.
    fn create(username: &str, settings: &NewAccountSettings) -> Self {
        require_polygon();
        let name = AccountName::parse(username).expect("the suite's own names are valid");
        remove(&name);
        operations()
            .create(&name, settings)
            .unwrap_or_else(|error| panic!("useradd must succeed in the polygon: {error}"));

        Self { name }
    }
}

impl Drop for LimitsAccount {
    /// Removes the account, its home and any drop-in written for it.
    fn drop(&mut self) {
        remove(&self.name);
    }
}

/// Refuses to go on unless this process is root inside a polygon image.
///
/// A panic and not a quiet `return`: these suites are `#[ignore]`d, so a skip
/// would report as a pass (rules/testing.md).
///
/// # Panics
///
/// Panics when the polygon marker is absent or the process is not root.
fn require_polygon() {
    assert!(
        !std::env::var(POLYGON_MARKER).unwrap_or_default().is_empty(),
        "these tests create real system accounts and write under /etc/security: \
         {POLYGON_MARKER} is not set. See docker/README.md."
    );
    assert_eq!(rustix::process::getuid().as_raw(), 0, "start as root");
}

/// The distribution adapter for the polygon this suite is running in.
fn polygon_distro() -> &'static dyn DistroAdapter {
    adapter_for(
        detect()
            .expect("a polygon image is a supported host")
            .family,
    )
}

/// The account operations, bound to the real host.
fn operations() -> AccountOperations<ProcessSystemHost> {
    AccountOperations::new(
        ProcessSystemHost::new(polygon_distro()),
        polygon_distro(),
        FreeSpaceFloor::default_floor(),
    )
}

/// Removes `name`'s system user and its drop-in, whatever state they are in.
fn remove(name: &AccountName) {
    let _ = std::fs::remove_file(AgentPaths::account_limits_file(name));
    let _ = Command::new(polygon_distro().userdel_binary())
        .args(["--remove", name.as_str()])
        .output();
}

/// The shell field of the account's password entry, as `getent` reports it.
///
/// # Panics
///
/// Panics when `getent` cannot be run or prints a malformed entry.
fn shell_of(name: &AccountName) -> String {
    let output = Command::new("getent")
        .args(["passwd", name.as_str()])
        .output()
        .expect("getent is installed in the polygon");
    let entry = String::from_utf8(output.stdout).expect("a passwd entry is text");

    entry
        .trim_end()
        .split(':')
        .nth(6)
        .unwrap_or_else(|| panic!("a malformed passwd entry: {entry:?}"))
        .to_owned()
}

/// The open-files soft limit a real `su` session of the account starts with.
///
/// # Panics
///
/// Panics when `su` cannot be run, which a polygon image never is.
fn session_open_files(name: &AccountName) -> String {
    let output = Command::new("su")
        .args(["-s", "/bin/bash", "-c", "ulimit -n", name.as_str()])
        .output()
        .expect("su is installed in the polygon");
    assert!(
        output.status.success(),
        "su must open a session for the account: {}",
        String::from_utf8_lossy(&output.stderr)
    );

    String::from_utf8(output.stdout)
        .expect("ulimit prints text")
        .trim()
        .to_owned()
}

fn bash_settings() -> NewAccountSettings {
    NewAccountSettings {
        shell: LoginShell::Bash,
        ..NewAccountSettings::with_quota(0)
    }
}

#[test]
#[ignore = "requires a polygon container"]
fn an_account_created_with_the_bash_login_has_the_distributions_bash() {
    let account = LimitsAccount::create("polylogin", &bash_settings());

    assert_eq!(shell_of(&account.name), polygon_distro().bash_shell());
    let bash = std::fs::metadata(polygon_distro().bash_shell())
        .expect("the shell an account is given must exist on the host");
    assert!(
        bash.is_file() && bash.permissions().mode() & 0o111 != 0,
        "the bash path the adapter documents must name an executable file"
    );
}

#[test]
#[ignore = "requires a polygon container"]
fn an_account_created_without_a_shell_choice_has_the_nologin_shell() {
    // The control of the case above: the same account, the settings an older
    // caller sends. A creation that always gave bash would pass the case above.
    let account = LimitsAccount::create("polynologin", &NewAccountSettings::with_quota(0));

    assert_eq!(shell_of(&account.name), polygon_distro().nologin_shell());
}

#[test]
#[ignore = "requires a polygon container"]
fn suspension_takes_the_shell_away_and_unsuspension_gives_back_the_stated_one() {
    let account = LimitsAccount::create("polysuspend", &bash_settings());
    let operations = operations();

    operations.suspend(&account.name).unwrap();
    assert_eq!(shell_of(&account.name), polygon_distro().nologin_shell());

    operations
        .unsuspend(&account.name, LoginShell::Bash)
        .unwrap();
    assert_eq!(
        shell_of(&account.name),
        polygon_distro().bash_shell(),
        "an account granted a shell must get it back, not lose it to the first cycle"
    );

    // The control: the same cycle for an account the caller says has no login.
    operations.suspend(&account.name).unwrap();
    operations
        .unsuspend(&account.name, LoginShell::Nologin)
        .unwrap();
    assert_eq!(shell_of(&account.name), polygon_distro().nologin_shell());
}

#[test]
#[ignore = "requires a polygon container"]
fn an_open_files_limit_is_what_a_real_session_starts_with_until_it_is_removed() {
    let account = LimitsAccount::create("polylimits", &NewAccountSettings::with_quota(0));
    let operations = operations();
    let before = session_open_files(&account.name);
    assert_ne!(
        before,
        WRITTEN_LIMIT.to_string(),
        "the limit the case writes must differ from the session's default, or it proves nothing"
    );

    set_account_open_files_limit(
        &operations,
        &account.name,
        ResourceCeiling::Limited(OpenFilesLimit::parse(WRITTEN_LIMIT).unwrap()),
    )
    .unwrap();

    let file = AgentPaths::account_limits_file(&account.name);
    let contents = std::fs::read_to_string(&file).unwrap();
    assert!(contents.contains(&format!(
        "{} - nofile {WRITTEN_LIMIT}\n",
        account.name.as_str()
    )));
    assert_eq!(
        std::fs::metadata(&file).unwrap().permissions().mode() & 0o777,
        0o644
    );
    assert_eq!(
        session_open_files(&account.name),
        WRITTEN_LIMIT.to_string(),
        "su's PAM stack must load pam_limits for the drop-in to reach a session; if it \
         does not on this family, the limit reaches no session and the feature's \
         documented reach is wrong"
    );

    set_account_open_files_limit(&operations, &account.name, ResourceCeiling::Unlimited).unwrap();

    assert!(!Path::new(&file).exists(), "no ceiling means no file");
    assert_eq!(session_open_files(&account.name), before);
}

#[test]
#[ignore = "requires a polygon container"]
fn removing_a_limit_that_was_never_set_is_not_an_error() {
    let account = LimitsAccount::create("polynolimit", &NewAccountSettings::with_quota(0));

    set_account_open_files_limit(&operations(), &account.name, ResourceCeiling::Unlimited).unwrap();
}
