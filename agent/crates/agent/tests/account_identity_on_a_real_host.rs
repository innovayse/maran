//! The account lifecycle refuses a user that is not a hosting account, on a
//! real host, and leaves that user exactly as it found it (issue #83).
//!
//! Every unit test in the tree can only show that the agent DECIDED to refuse a
//! name; what nothing else can show is that the real `usermod`, `userdel`,
//! `crontab -u` and `passwd` were never run against the real user. So every
//! refusal here is judged by looking at the host afterwards (the passwd entry,
//! the shadow state, the crontab, the home directory), never by the returned
//! error alone: an error can be returned after the damage is done, which is
//! exactly how the defect looked before this change.
//!
//! The claims, each paired with an accepting control so a refusal that fires for
//! everything cannot pass:
//!
//! - `root` is refused by every lifecycle operation and by the cron reader and
//!   writer, and its passwd entry, shadow state and crontab are byte-identical
//!   afterwards.
//! - A user made by hand under the name of a cloud image's default login is
//!   refused although it clears every structural check (uid above the floor, home
//!   exactly `/home/<name>`): only the reserved list stops it.
//! - A system user whose home happens to be under `/home` is refused by the id
//!   floor, and an ordinary-id user whose home is elsewhere is refused by the
//!   home rule.
//! - A reserved name is refused by `create` before `useradd`, so nothing is made.
//! - An account the panel created is still suspended and resumed: the gate does
//!   not refuse the legitimate path.
//!
//! These tests need to run as root inside a polygon container, because they
//! create real system users and plant a real crontab for root.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// This suite needs only the fixture's constructor and name; the ids and home
// accessors belong to other suites' claims. The allow sits on the module so an
// item no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;

use std::io::Write;
use std::process::{Command, Stdio};

use maran_agent_core::validation::system::managed_account_error::ManagedAccountError;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountError, AccountOperations, ProcessSystemHost};
use maran_ops::cron::{CronError, ProcessCronHost, list_cron_entries, set_cron_environment};
use maran_ops::db::ProcessDbHost;
use maran_ops::ftps::ProcessFtpsHost;
use maran_ops::php::ProcessPhpHost;
use maran_ops::sftp::ProcessSftpHost;

use polygon_account::PolygonAccount;

/// A reserved name on the cloud-image class of the list that no polygon image
/// carries, so a hand-made user can take it.
const CLOUD_LOGIN: &str = "ec2_user";

/// A reserved name that no polygon image carries, used to prove `create` makes
/// nothing for it.
const ABSENT_RESERVED: &str = "azureuser";

/// An ordinary spelling that is not reserved, for the user whose home is
/// elsewhere.
const ELSEWHERE: &str = "polyelsewhere";

/// An ordinary spelling that is not reserved, for the system user with a home
/// under `/home`.
const SYSTEM_USER: &str = "polysysacct";

/// The line planted in root's crontab, so "root's table is untouched" is a
/// statement about a table that had something in it.
const ROOT_CRON_LINE: &str = "17 3 * * * /bin/true # planted by the identity suite\n";

/// A password hash no one can match, accepted by `usermod --password` as the
/// stored form of a hand-set password.
const HAND_SET_HASH: &str = "$6$polyidentity$unmatchable";

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

/// The account operations, bound to the real host.
fn operations() -> AccountOperations<ProcessSystemHost> {
    AccountOperations::new(ProcessSystemHost::new(polygon_distro()), polygon_distro())
}

/// Parses a name this suite wrote itself.
fn name(text: &str) -> AccountName {
    AccountName::parse(text).expect("the suite's own names are valid")
}

/// Runs a program and returns its exit code, stdout and stderr as one string, so
/// two observations of the same thing can be compared whole.
fn observe(program: &str, arguments: &[&str]) -> String {
    let outcome = Command::new(program)
        .args(arguments)
        .output()
        .unwrap_or_else(|error| panic!("{program} must be installed in the polygon: {error}"));

    format!(
        "exit={:?}\nstdout={}\nstderr={}",
        outcome.status.code(),
        String::from_utf8_lossy(&outcome.stdout),
        String::from_utf8_lossy(&outcome.stderr)
    )
}

/// Everything about one user that a lifecycle operation could change and that
/// this suite can read back: the passwd entry, the shadow-derived status and the
/// crontab.
fn observe_user(user: &str) -> String {
    format!(
        "{}\n{}\n{}",
        observe("getent", &["passwd", user]),
        observe("passwd", &["-S", user]),
        observe("crontab", &["-u", user, "-l"]),
    )
}

/// Whether the user database holds an entry for `user`.
fn user_exists(user: &str) -> bool {
    Command::new("getent")
        .args(["passwd", user])
        .output()
        .expect("getent is installed in every polygon image")
        .status
        .success()
}

/// Runs a user-management program that must succeed.
fn must_run(program: &str, arguments: &[&str]) {
    let outcome = Command::new(program)
        .args(arguments)
        .output()
        .unwrap_or_else(|error| panic!("{program} must be installed in the polygon: {error}"));

    assert!(
        outcome.status.success(),
        "{program} {arguments:?} must succeed in the polygon: {}",
        String::from_utf8_lossy(&outcome.stderr)
    );
}

/// Installs `text` as root's crontab through the real `crontab(1)`.
fn plant_root_crontab(text: &str) {
    let mut child = Command::new("crontab")
        .args(["-u", "root", "-"])
        .stdin(Stdio::piped())
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .spawn()
        .expect("crontab is installed in every polygon image");
    child
        .stdin
        .take()
        .expect("stdin was piped")
        .write_all(text.as_bytes())
        .unwrap();
    assert!(
        child.wait().unwrap().success(),
        "planting root's crontab must succeed in the polygon"
    );
}

/// Removes a hand-made user and its home, tolerating that it is already gone.
fn remove_by_hand(user: &str) {
    let outcome = Command::new("userdel")
        .args(["--remove", user])
        .output()
        .expect("userdel is installed in every polygon image");
    let status = outcome.status.code().unwrap_or(-1);
    assert!(
        matches!(status, 0 | 6 | 12),
        "a leftover polygon user must be removable, userdel exited {status}: {}",
        String::from_utf8_lossy(&outcome.stderr)
    );
}

/// Whether an operation refused because the name is not a managed account, for
/// the given reason.
fn refused_as<T: std::fmt::Debug>(
    outcome: &Result<T, AccountError>,
    expected: &ManagedAccountError,
) -> bool {
    matches!(
        outcome,
        Err(AccountError::NotManaged { refusal, .. }) if refusal == expected
    )
}

/// Runs every lifecycle operation against `target` and asserts each refused for
/// `expected`.
///
/// `delete` is handed the real hosts for every neighbouring area: a refusal that
/// fires before the first cascade step never calls them, and one that fired late
/// would do real work through them, which the caller's before/after comparison
/// then catches.
fn assert_every_lifecycle_operation_refuses(target: &AccountName, expected: &ManagedAccountError) {
    let operations = operations();

    assert!(
        refused_as(&operations.suspend(target), expected),
        "suspend must refuse {}",
        target.as_str()
    );
    assert!(
        refused_as(&operations.unsuspend(target), expected),
        "unsuspend must refuse {}",
        target.as_str()
    );
    assert!(
        refused_as(&operations.set_quota(target, 1024 * 1024), expected),
        "set_quota must refuse {}",
        target.as_str()
    );
    assert!(
        refused_as(&operations.usage(target), expected),
        "usage must refuse {}",
        target.as_str()
    );
    assert!(
        refused_as(
            &operations.delete(
                &ProcessPhpHost::new(),
                &ProcessDbHost::new(polygon_distro()),
                &ProcessSftpHost::new(),
                &ProcessFtpsHost::new(),
                target,
            ),
            expected
        ),
        "delete must refuse {}",
        target.as_str()
    );
}

#[test]
#[ignore = "runs the account lifecycle against root on a real host: polygon only"]
fn the_account_lifecycle_and_the_cron_surface_refuse_root_and_change_nothing_about_it() {
    PolygonAccount::require_polygon();
    let root = name("root");
    let previous = observe("crontab", &["-u", "root", "-l"]);
    plant_root_crontab(ROOT_CRON_LINE);
    let before = observe_user("root");
    assert!(
        before.contains("planted by the identity suite"),
        "the planted line must be in root's table before the refusals, or the comparison below \
         compares two empty tables: {before}"
    );

    assert_every_lifecycle_operation_refuses(&root, &ManagedAccountError::Reserved);

    let host = ProcessCronHost::new(polygon_distro());
    assert!(
        matches!(
            list_cron_entries(&host, &root),
            Err(CronError::Account(ManagedAccountError::Reserved))
        ),
        "listing root's cron entries must be refused, not answered from root's table"
    );
    assert!(
        matches!(
            set_cron_environment(&host, polygon_distro(), &root, Vec::new()),
            Err(CronError::Account(ManagedAccountError::Reserved))
        ),
        "rewriting root's cron environment must be refused"
    );

    let after = observe_user("root");
    assert_eq!(
        before, after,
        "root's passwd entry, shadow status and crontab must be byte-identical after the refusals"
    );

    // Leave the container as it was found.
    if previous.contains("exit=Some(0)") {
        let table = previous
            .split("stdout=")
            .nth(1)
            .and_then(|rest| rest.split("\nstderr=").next())
            .unwrap_or_default();
        plant_root_crontab(table);
    } else {
        must_run("crontab", &["-u", "root", "-r"]);
    }
}

#[test]
#[ignore = "creates a real user by hand and runs the lifecycle against it: polygon only"]
fn a_user_made_by_hand_under_a_cloud_images_login_name_is_refused_although_it_clears_every_structural_check()
 {
    PolygonAccount::require_polygon();
    remove_by_hand(CLOUD_LOGIN);
    must_run("useradd", &["--create-home", "--user-group", CLOUD_LOGIN]);
    let home = format!("/home/{CLOUD_LOGIN}");
    std::fs::write(format!("{home}/keep.txt"), "the operator's own file\n").unwrap();
    let before = observe_user(CLOUD_LOGIN);

    assert_every_lifecycle_operation_refuses(&name(CLOUD_LOGIN), &ManagedAccountError::Reserved);

    assert_eq!(
        before,
        observe_user(CLOUD_LOGIN),
        "the hand-made user must be exactly as it was"
    );
    assert!(
        std::path::Path::new(&format!("{home}/keep.txt")).exists(),
        "the hand-made user's home must survive a refused deletion"
    );
    remove_by_hand(CLOUD_LOGIN);
}

#[test]
#[ignore = "creates a real system user with a home under /home: polygon only"]
fn a_system_user_with_a_home_under_home_is_refused_by_the_id_floor_and_left_alone() {
    PolygonAccount::require_polygon();
    remove_by_hand(SYSTEM_USER);
    let home = format!("/home/{SYSTEM_USER}");
    must_run(
        "useradd",
        &[
            "--system",
            "--create-home",
            "--home-dir",
            &home,
            "--user-group",
            SYSTEM_USER,
        ],
    );
    let before = observe_user(SYSTEM_USER);

    assert_every_lifecycle_operation_refuses(
        &name(SYSTEM_USER),
        &ManagedAccountError::SystemIdentity,
    );

    assert_eq!(before, observe_user(SYSTEM_USER));
    assert!(
        std::path::Path::new(&home).exists(),
        "a refused deletion must leave the system user's home"
    );
    remove_by_hand(SYSTEM_USER);
}

#[test]
#[ignore = "creates a real user whose home is elsewhere: polygon only"]
fn an_ordinary_user_whose_home_is_not_under_home_is_refused_by_the_home_rule_and_left_alone() {
    PolygonAccount::require_polygon();
    remove_by_hand(ELSEWHERE);
    let home = format!("/srv/{ELSEWHERE}");
    must_run(
        "useradd",
        &[
            "--create-home",
            "--home-dir",
            &home,
            "--user-group",
            ELSEWHERE,
        ],
    );
    let before = observe_user(ELSEWHERE);

    assert_every_lifecycle_operation_refuses(&name(ELSEWHERE), &ManagedAccountError::WrongHome);

    assert_eq!(before, observe_user(ELSEWHERE));
    assert!(
        std::path::Path::new(&home).exists(),
        "a refused deletion must leave the user's home"
    );
    remove_by_hand(ELSEWHERE);
}

#[test]
#[ignore = "asks useradd to make a reserved name and proves it ran nothing: polygon only"]
fn creating_an_account_with_a_reserved_name_runs_no_useradd() {
    PolygonAccount::require_polygon();
    assert!(
        !user_exists(ABSENT_RESERVED),
        "this check needs a polygon image without a {ABSENT_RESERVED} user, or it proves nothing"
    );

    let refused = operations().create(&name(ABSENT_RESERVED), 0);

    assert!(
        refused_as(&refused, &ManagedAccountError::Reserved),
        "a reserved name must be refused as reserved, not made and not reported as existing: \
         {refused:?}"
    );
    assert!(
        !user_exists(ABSENT_RESERVED),
        "the refusal must come before useradd: the reserved name was made"
    );
}

#[test]
#[ignore = "creates a real account the panel's way and suspends it: polygon only"]
fn an_account_the_panel_created_is_still_suspended_and_resumed() {
    let account = PolygonAccount::create("polyidentity");
    let operations = operations();
    let user = account.name().as_str();

    // A usable password makes the lock visible: an account without one reads as
    // locked before and after, and the comparison below would say nothing.
    must_run("usermod", &["--password", HAND_SET_HASH, user]);
    let usable = observe("passwd", &["-S", user]);

    operations
        .suspend(account.name())
        .unwrap_or_else(|error| panic!("a managed account must be suspendable: {error}"));
    let suspended = observe("passwd", &["-S", user]);
    operations
        .unsuspend(account.name())
        .unwrap_or_else(|error| panic!("a managed account must be resumable: {error}"));
    let resumed = observe("passwd", &["-S", user]);

    assert_ne!(
        usable, suspended,
        "suspension must visibly lock the account, or the refusals above were compared against a \
         gate that never opens"
    );
    assert_eq!(
        usable, resumed,
        "resumption must restore the usable state the account had before it was suspended"
    );
}
