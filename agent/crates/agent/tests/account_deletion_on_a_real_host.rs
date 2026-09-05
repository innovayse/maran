//! The account-deletion cascade against a real MariaDB, a real OpenSSH daemon
//! and a real bind mount — the only place it means anything.
//!
//! `userdel` touches neither MySQL nor sshd. Every unit test in the tree can
//! only show that the agent DECIDED to drop a database and revoke a login; what
//! nothing else can show is that the server and the daemon agree afterwards,
//! and that an account created again under the same name — system user names
//! are recycled — inherits none of it.
//!
//! Three claims are settled here and nowhere else:
//!
//! - **The databases are really gone from the server**, and the credential that
//!   reached them really stops working. Asserted as a REFUSED login, not as a
//!   name missing from a listing: a listing can be wrong in the direction that
//!   passes.
//! - **The SFTP login is really refused by sshd**, in a real session. Never as
//!   the absence of a line from a configuration file — a directive in the wrong
//!   block reads the same and does nothing, and a login that has merely been
//!   forgotten by the panel still works.
//! - **The crontab is really gone from the host's spool**, and a same-named
//!   account created afterwards has none. `userdel` removes neither family's
//!   spool file — the Debian family keeps `/var/spool/cron/crontabs/<name>`
//!   owned by the account's freed uid, the RHEL family `/var/spool/cron/<name>`
//!   owned by root — and cron keys that file by NAME, so the survivor is handed
//!   whole to whoever holds the name next. Only a real `crontab(1)` and a real
//!   `userdel` can show that, which is why the claim lives here.
//! - **The bind mount is really down and the jail is really gone.** A mount that
//!   survives the deletion is a mount of a home `userdel` has just removed, and
//!   the uninstaller refuses to remove `/var/lib/maran` while any mount remains
//!   under it.
//!
//! These tests need `docker run --privileged`, for the same reason the SFTP
//! suite does: the jail is a real bind mount. Without it the login cannot be
//! created at all and the tests fail loudly rather than passing on a jail that
//! was never filled.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_mariadb.rs"]
mod polygon_mariadb;
// `exec` — the fixture's "can this login run a command" probe — belongs to the
// SFTP suite's own claims and is not one of this suite's, so it is unused here.
// The allow is on the module rather than on the fixture, so a fixture item that
// no suite uses is still reported where it is declared.
#[allow(dead_code)]
#[path = "fixtures/polygon_sshd.rs"]
mod polygon_sshd;

use std::path::Path;
use std::process::Command;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::cron_command::CronCommand;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountOperations, ProcessSystemHost};
use maran_ops::cron::{ProcessCronHost, create_cron_entry, list_cron_entries};
use maran_ops::db::{CreateDatabaseRequest, ProcessDbHost, create_database};
use maran_ops::php::ProcessPhpHost;
use maran_ops::sftp::{AccountJail, ProcessSftpHost, SftpUserRequest, create_sftp_user};

use polygon_account::PolygonAccount;
use polygon_mariadb::PolygonMariadb;
use polygon_sshd::PolygonSshd;

/// The account this suite creates, deletes, and creates again under the same
/// name. One name for both lives on purpose: recycling is the whole subject.
const ACCOUNT: &str = "polycascade";

/// The suffix of the database the account is given.
const DATABASE: &str = "shop";

/// The suffix of the database user the account is given.
const DATABASE_USER: &str = "shopuser";

/// The suffix of the SFTP login the account is given.
const SFTP_LOGIN: &str = "web";

/// The password both credentials are created with.
///
/// It uses every character class `Password` allows, so a pipe or a shell that
/// ate the punctuation would show up as a login that does not work rather than
/// as nothing at all.
const CUSTOMER_PASSWORD: &str = "Str0ng-pass.word=+_";

/// What the account's data looks like, so "the database is gone" is a statement
/// about a database that had something in it.
const CUSTOMER_TABLE: &str = "orders";

/// The file planted in the account's home, so the SFTP session before the
/// deletion is looking at real customer data rather than an empty directory.
const CUSTOMER_FILE: &str = "hello.txt";

/// The command the account's scheduled entry runs.
///
/// Its text never reaches the crontab line — it is written to a `0600` file
/// under the account's home and the line names that file — so what survives a
/// deletion is a schedule pointing at a path, not a command. That is why the
/// assertions below are about the TABLE existing at all rather than about this
/// string appearing in it.
const CUSTOMER_CRON_COMMAND: &str = "/bin/echo scheduled by the previous tenant";

/// The sentence both cron lineages print for an account with no table.
///
/// Written out again here rather than imported: a test that took the constant
/// the implementation matches on would agree with it by construction and could
/// never notice the program changing its mind. This one is compared against
/// what the real tool printed.
const NO_CRONTAB_MARKER: &str = "no crontab for";

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

/// Runs the deletion under test against the real machine.
///
/// # Panics
///
/// Panics when the deletion refuses, quoting what refused — a `JailFailed` here
/// usually means the container was started without `--privileged`.
fn delete_the_account(name: &AccountName) {
    operations()
        .delete(
            &ProcessPhpHost::new(),
            &ProcessDbHost::new(polygon_distro()),
            &ProcessSftpHost::new(),
            name,
        )
        .unwrap_or_else(|error| panic!("the account deletion must succeed: {error}"));
}

/// The fully-qualified name of `account`'s database.
fn database_of(account: &AccountName) -> String {
    format!("{}_{DATABASE}", account.as_str())
}

/// The fully-qualified name of `account`'s database user.
fn database_user_of(account: &AccountName) -> String {
    format!("{}_{DATABASE_USER}", account.as_str())
}

/// The fully-qualified name of `account`'s SFTP login.
fn sftp_login_of(account: &AccountName) -> String {
    format!("{}_{SFTP_LOGIN}", account.as_str())
}

/// What `crontab -u <account> -l` printed, both streams and the status.
///
/// A separate process on purpose: it asks the same program the agent asks,
/// through its documented interface, rather than reading the spool directly.
/// Where that spool lives, what owns it and what a bare uid in it renders as
/// differ between the two families — `/var/spool/cron/crontabs/<name>` owned by
/// the account on the Debian family, `/var/spool/cron/<name>` owned by root on
/// the RHEL one — so a test that stat-ed a path would be asserting one family's
/// layout and passing vacuously on the other.
///
/// # Panics
///
/// Panics when the program cannot be run at all.
fn crontab_of(account: &AccountName) -> std::process::Output {
    Command::new(polygon_distro().crontab_binary())
        .args(["-u", account.as_str(), "-l"])
        .env("LC_ALL", "C")
        .output()
        .expect("the polygon image installs crontab")
}

/// Whether the host's cron spool holds a table for `account` at all.
///
/// True when the program printed one, false only when it said the account has
/// none. Any other refusal is a failed test rather than a `false`: "the program
/// would not answer" must never be read as "there is nothing there", which is
/// the direction this whole area fails in.
///
/// # Panics
///
/// Panics when `crontab -l` refused for a reason that is not the absent table.
fn has_a_crontab(account: &AccountName) -> bool {
    let listed = crontab_of(account);
    if listed.status.success() {
        return true;
    }

    assert!(
        String::from_utf8_lossy(&listed.stderr).contains(NO_CRONTAB_MARKER),
        "crontab -l refused for a reason other than an absent table: {}",
        said(&listed)
    );

    false
}

/// Gives `account` a database with a table in it and an SFTP login, through the
/// same operations the panel drives.
///
/// # Panics
///
/// Panics when either creation refuses.
fn provision(server: &PolygonMariadb, account: &AccountName) {
    let request = CreateDatabaseRequest {
        database: DatabaseName::for_account(account, DATABASE).expect("a valid database name"),
        user: DbUserName::for_account(account, DATABASE_USER).expect("a valid user name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    create_database(&ProcessDbHost::new(polygon_distro()), &request)
        .unwrap_or_else(|error| panic!("creating the account's database must succeed: {error}"));

    let created = server.run(&format!(
        "CREATE TABLE `{}`.{CUSTOMER_TABLE} (id INT)",
        database_of(account)
    ));
    assert!(
        created.status.success(),
        "the fixture table must be created:\n{}",
        String::from_utf8_lossy(&created.stderr)
    );

    let login = SftpUserRequest {
        account: account.clone(),
        user: SftpUserName::for_account(account, SFTP_LOGIN).expect("a valid login name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &login).unwrap_or_else(|error| {
        panic!(
            "creating the account's SFTP login must succeed: {error}. A JailFailed \
             here usually means the container was started without --privileged, so \
             the bind mount could not be made."
        )
    });

    create_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account,
        &CronSchedule::parse("*", "*", "*", "*", "*").expect("a valid schedule"),
        &CronCommand::parse(CUSTOMER_CRON_COMMAND).expect("a valid command"),
    )
    .unwrap_or_else(|error| panic!("giving the account a cron entry must succeed: {error}"));
}

/// Puts a file in `account`'s home, owned by the account.
///
/// # Panics
///
/// Panics when the file cannot be written or given to the account.
fn plant_file(account: &PolygonAccount) {
    let path = account.home().join(CUSTOMER_FILE);
    std::fs::write(&path, "customer data").expect("the account's home must be writable by root");
    std::os::unix::fs::chown(&path, Some(account.ids().uid()), Some(account.ids().gid()))
        .expect("the planted file must belong to the account");
}

/// Whether anything is mounted at `path` right now, read from the kernel.
fn is_mounted(path: &str) -> bool {
    let mounts = std::fs::read_to_string("/proc/self/mountinfo").expect("/proc must be mounted");

    mounts
        .lines()
        .any(|line| line.split_whitespace().any(|field| field == path))
}

/// Whether the server holds a database called `name`.
fn server_holds(server: &PolygonMariadb, name: &str) -> bool {
    let listing = server.run("SHOW DATABASES");

    String::from_utf8_lossy(&listing.stdout)
        .lines()
        .any(|line| line.trim() == name)
}

/// Everything a client printed, both streams together.
fn said(output: &std::process::Output) -> String {
    format!(
        "{}{}",
        String::from_utf8_lossy(&output.stdout),
        String::from_utf8_lossy(&output.stderr)
    )
}

#[test]
#[ignore = "creates and deletes a real account with a real database and a real login: polygon only"]
fn a_deleted_account_leaves_no_database_and_a_recreated_account_of_the_same_name_inherits_nothing()
{
    let server = PolygonMariadb::start();
    let sshd = PolygonSshd::start();

    // The fixture is held for the whole test: its `Drop` removes whatever holds
    // this name at the end, which after the re-creation below is the SECOND
    // account. That is what stops the suite from leaving a real account behind.
    let account = PolygonAccount::create(ACCOUNT);
    let name = account.name().clone();
    plant_file(&account);
    provision(&server, &name);

    // Everything is really there and really works FIRST. Without this half,
    // "the new account inherits nothing" would also pass on a host where the
    // provisioning silently did nothing at all.
    assert!(server_holds(&server, &database_of(&name)));
    let before = server.run_as(
        &database_user_of(&name),
        CUSTOMER_PASSWORD,
        &format!(
            "SELECT COUNT(*) FROM `{}`.{CUSTOMER_TABLE}",
            database_of(&name)
        ),
    );
    assert!(
        before.status.success(),
        "the customer's credential must reach the customer's data before the deletion:\n{}",
        said(&before)
    );
    let session = sshd.sftp(&sftp_login_of(&name), CUSTOMER_PASSWORD, "cd home\nls\n");
    assert!(
        session.status.success(),
        "the SFTP login must work before the deletion:\n{}",
        said(&session)
    );
    assert!(
        has_a_crontab(&name),
        "the account must really have a crontab before the deletion, or nothing \
         below about inheriting one proves anything"
    );
    assert!(
        said(&session).contains(CUSTOMER_FILE),
        "the login must reach the account's real files through the bind mount, or \
         the jail was never filled and nothing below proves anything:\n{}",
        said(&session)
    );

    delete_the_account(&name);

    // Re-created under the same name, exactly as a hosting panel recycles one.
    operations()
        .create(&name, 0)
        .unwrap_or_else(|error| panic!("re-creating the account must succeed: {error}"));

    // 1. No database of the old tenant's survives for the new one to open.
    assert!(
        !server_holds(&server, &database_of(&name)),
        "a re-created account must not inherit the previous tenant's database"
    );

    // 2. And the credential that reached it is refused. Asserted as a refusal,
    //    because a name missing from a listing is compatible with a user that
    //    still authenticates and still has its grant.
    let after = server.run_as(&database_user_of(&name), CUSTOMER_PASSWORD, "SELECT 1");
    assert!(
        !after.status.success(),
        "the previous tenant's database credential must no longer authenticate:\n{}",
        said(&after)
    );
    let survivors = server.run(&format!(
        "SELECT COUNT(*) FROM mysql.user WHERE User = '{}'",
        database_user_of(&name)
    ));
    assert_eq!(
        String::from_utf8_lossy(&survivors.stdout).trim(),
        "0",
        "the previous tenant's database user must be gone from the server"
    );

    // 3. The SFTP login is refused by the daemon in a real session — never
    //    asserted as a missing configuration line.
    let refused = sshd.sftp(&sftp_login_of(&name), CUSTOMER_PASSWORD, "cd home\nls\n");
    assert!(
        !refused.status.success(),
        "the previous tenant's SFTP credential must no longer log in:\n{}",
        said(&refused)
    );

    // 4. The old home went with the old account, so the new one starts empty.
    assert!(
        !account.home().join(CUSTOMER_FILE).exists(),
        "a re-created account must not inherit the previous tenant's files"
    );

    // 5. The previous tenant's schedule did not come with the name. `userdel`
    //    removes neither family's spool file — measured, on both — so without
    //    the cascade removing it the table survives under the account's NAME,
    //    and cron hands it to whoever holds that name next. The panel then
    //    renders it on the new account's own scheduled-tasks screen as an entry
    //    it never created.
    //
    //    Asserted through the program rather than through a path: the two
    //    families disagree about where the file lives and what owns it, and on
    //    the Debian family the survivor's numeric uid is simply re-rendered as
    //    the new account's name — nothing chowns anything — so a stat-based
    //    assertion would be describing `ls` rather than the defect.
    assert!(
        !has_a_crontab(&name),
        "a re-created account must not inherit the previous tenant's crontab"
    );
    assert!(
        list_cron_entries(&ProcessCronHost::new(polygon_distro()), &name)
            .expect("listing a fresh account's entries must succeed")
            .is_empty(),
        "the new account's scheduled tasks must be empty: an inherited row names \
         a command file that went with the old home, so the panel would show an \
         entry that cannot run and that nobody created"
    );

    // 6. The jail is gone, so the new account gets a fresh one rather than the
    //    old tenant's — and nothing is mounted where the old home was.
    let jail = AccountJail::for_account(&name, polygon_distro().systemd_unit_directory());
    assert!(
        !is_mounted(jail.mount_point()),
        "the bind mount must be down before `userdel` removes the home it points at"
    );
    assert!(
        !Path::new(jail.directory()).exists(),
        "the jail must be gone: a re-created account must not land in the old one"
    );
    assert!(
        !Path::new(jail.unit_path()).exists(),
        "the mount unit must be gone with the jail it filled"
    );
}

#[test]
#[ignore = "creates and deletes a real account with a real database and a real login: polygon only"]
fn deleting_an_account_leaves_a_neighbouring_account_whose_name_it_prefixes_untouched() {
    // `polycascade_` is a prefix of every name belonging to the account
    // `polycascade_two`, so a cascade that scanned by prefix would drop a
    // neighbour's database and revoke their login as a side effect of deleting
    // this one. The decode splits at the LAST separator instead, and only a real
    // host can show that the two accounts' names really do collide the way this
    // claims — `polycascade_two_shopuser` is a name MySQL will hold.
    let server = PolygonMariadb::start();
    let sshd = PolygonSshd::start();

    let mine = PolygonAccount::create(ACCOUNT);
    let neighbour = PolygonAccount::create(&format!("{ACCOUNT}_two"));
    let my_name = mine.name().clone();
    let their_name = neighbour.name().clone();
    provision(&server, &my_name);
    provision(&server, &their_name);

    delete_the_account(&my_name);

    assert!(
        server_holds(&server, &database_of(&their_name)),
        "the neighbour's database must survive this account's deletion"
    );
    let theirs = sshd.sftp(
        &sftp_login_of(&their_name),
        CUSTOMER_PASSWORD,
        "cd home\nls\n",
    );
    assert!(
        theirs.status.success(),
        "the neighbour's SFTP login must still work:\n{}",
        said(&theirs)
    );

    assert!(
        has_a_crontab(&their_name),
        "the neighbour's crontab must survive this account's deletion: the spool \
         is keyed by name, and a removal that matched a prefix — or that ran \
         against a uid the deleted account had just freed — would take theirs too"
    );

    let jail = AccountJail::for_account(&their_name, polygon_distro().systemd_unit_directory());
    assert!(
        is_mounted(jail.mount_point()),
        "the neighbour's bind mount must still be in place"
    );

    // Cleaned up here rather than left to the fixture, whose teardown is
    // deliberately narrower than the cascade (see `polygon_account.rs`).
    delete_the_account(&their_name);
}
