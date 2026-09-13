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
#[path = "fixtures/polygon_config_file.rs"]
mod polygon_config_file;
#[path = "fixtures/polygon_mariadb.rs"]
mod polygon_mariadb;
// The pool suite reads the version constant off this fixture as well; this
// suite needs only the validator and the parsed version, so the allow sits on
// the module rather than on the fixture's items.
#[allow(dead_code)]
#[path = "fixtures/polygon_php.rs"]
mod polygon_php;
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
use maran_agent_core::validation::system::ftps_user_name::FtpsUserName;
use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::system::sftp_user_name::SftpUserName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::accounts::{AccountError, AccountOperations, ProcessSystemHost};
use maran_ops::cron::{ProcessCronHost, create_cron_entry, list_cron_entries};
use maran_ops::db::{CreateDatabaseRequest, ProcessDbHost, create_database};
use maran_ops::ftps::{FtpsError, FtpsJail, FtpsUserRequest, ProcessFtpsHost, create_ftps_user};
use maran_ops::php::{PhpOpError, PoolInput, PoolPaths, ProcessPhpHost, write_pool};
use maran_ops::sftp::{AccountJail, ProcessSftpHost, SftpError, SftpUserRequest, create_sftp_user};

use polygon_account::PolygonAccount;
use polygon_config_file::PolygonConfigFile;
use polygon_mariadb::PolygonMariadb;
use polygon_php::PolygonPhp;
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

/// The suffix of the FTPS login the account is given.
///
/// A different suffix from the SFTP one deliberately: the two logins live in
/// two different jails and the cascade removes them in two separate steps, so a
/// shared name would make a passing assertion ambiguous about which step had
/// run.
const FTPS_LOGIN: &str = "files";

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
            &ProcessFtpsHost::new(),
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

/// The fully-qualified name of `account`'s FTPS login.
fn ftps_login_of(account: &AccountName) -> String {
    format!("{}_{FTPS_LOGIN}", account.as_str())
}

/// The tool that creates a group, at the path both supported families install
/// it to.
///
/// A literal here and not a `DistroAdapter` method, deliberately: no operation
/// in this agent creates a group — the installer does — so adding one to the
/// adapter would be widening the production surface for a test fixture. It sits
/// beside the suite's other absolute test-only binaries (`polygon_sshd.rs`'s
/// sshd, `polygon_cron.rs`'s two cron daemons) for the same reason.
const GROUPADD_BINARY: &str = "/usr/sbin/groupadd";

/// Makes sure the group an FTPS login is authorised by exists on this host.
///
/// The installer's FTPS step creates it, and the polygon images this suite runs
/// in are not rebuilt for this change — an image bakes the installer, and
/// rebuilding one is forbidden while another task owns `installer/`. So the
/// fixture creates the group itself, idempotently, rather than the suite
/// failing on an image that predates the step. `groupadd -f` is success for a
/// group that is already there.
///
/// # Panics
///
/// Panics when `groupadd` cannot be run or refuses.
fn ensure_ftps_group() {
    let created = Command::new(GROUPADD_BINARY)
        .args(["-f", polygon_distro().ftps_group()])
        .env("LC_ALL", "C")
        .output()
        .expect("the polygon image installs groupadd");
    assert!(
        created.status.success(),
        "the FTPS group must exist before a login can join it:\n{}",
        said(&created)
    );
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

    ensure_ftps_group();
    let ftps = FtpsUserRequest {
        account: account.clone(),
        user: FtpsUserName::for_account(account, FTPS_LOGIN).expect("a valid login name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    create_ftps_user(&ProcessFtpsHost::new(), polygon_distro(), &ftps).unwrap_or_else(|error| {
        panic!(
            "creating the account's FTPS login must succeed: {error}. A JailFailed \
             here usually means the container was started without --privileged, so \
             the second bind mount could not be made."
        )
    });

    create_cron_entry(
        &ProcessCronHost::new(polygon_distro()),
        polygon_distro(),
        account,
        &CronSchedule::parse("*", "*", "*", "*", "*").expect("a valid schedule"),
        &CronCommand::parse(CUSTOMER_CRON_COMMAND).expect("a valid command"),
        None,
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

    // The FTPS half, established as really there before anything claims it is
    // gone. Asserted on the machine — a passwd entry, a live mount read out of
    // /proc, a unit file — and not on the return value of the creation, which
    // would be `Ok` against a cascade that had built nothing at all.
    let ftps_jail = FtpsJail::for_account(&name, polygon_distro().systemd_unit_directory());
    assert!(
        passwd_holds(&ftps_login_of(&name)),
        "the FTPS login must be in the password database before the deletion"
    );
    assert!(
        is_mounted(ftps_jail.mount_point()),
        "the FTPS bind mount must be up before the deletion, or nothing below \
         about it coming down proves anything"
    );
    assert!(
        Path::new(ftps_jail.unit_path()).exists(),
        "the FTPS mount unit must be installed before the deletion"
    );
    assert!(
        ftps_jail.directory() != jail_of(&name).directory(),
        "the two protocols must chroot into two different directories, or the \
         teardowns below are one teardown asserted twice"
    );
    // The login is usable in the only sense this suite can settle without a
    // running vsftpd: what a client chrooted into that jail would find at
    // `home` is the account's real file, through the mount this step made.
    // The protocol-level login belongs to the FTPS polygon work.
    assert!(
        Path::new(ftps_jail.mount_point())
            .join(CUSTOMER_FILE)
            .exists(),
        "the customer's own file must be visible inside the FTPS jail, or the \
         jail was never filled"
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

    // 7. And the same four facts for FTPS, which `userdel` touches no more than
    //    it touches sshd. The login is a `--non-unique` passwd entry carrying
    //    the uid the deletion has just freed, in the group the FTPS PAM stack
    //    authorises; the mount is a bind mount of a home that no longer exists;
    //    the unit re-establishes that mount on every boot. Every one of them
    //    survives `userdel` on its own, and the host recycles names.
    assert!(
        !passwd_holds(&ftps_login_of(&name)),
        "an orphaned FTPS login survived the deletion: it carries the freed uid \
         and the group that authorises FTPS, so the next tenant of this name \
         inherits a live credential into their own files"
    );
    assert!(
        !is_mounted(ftps_jail.mount_point()),
        "the FTPS bind mount must be down before `userdel` removes the home it \
         points at"
    );
    assert!(
        !Path::new(ftps_jail.unit_path()).exists(),
        "the FTPS mount unit must be gone with the jail it filled: left behind, \
         it mounts whichever home holds this name into a jail nothing owns"
    );
    assert!(
        !Path::new(ftps_jail.directory()).exists(),
        "the FTPS jail must be gone: a re-created account must not land in the \
         old one"
    );
}

/// The SFTP jail of `account`, derived exactly as the operations derive it.
fn jail_of(account: &AccountName) -> AccountJail {
    AccountJail::for_account(account, polygon_distro().systemd_unit_directory())
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

    // The FTPS half of the same claim. `polycascade_` is a prefix of the
    // neighbour's every login name, and both accounts' FTPS logins carry a uid
    // in the same range and sit under the same jail root — so a teardown that
    // matched a prefix, or that enumerated by uid, would revoke the neighbour's
    // FTPS credential and unmount their home as a side effect of this deletion.
    assert!(
        passwd_holds(&ftps_login_of(&their_name)),
        "the neighbour's FTPS login must survive this account's deletion"
    );
    let their_ftps_jail =
        FtpsJail::for_account(&their_name, polygon_distro().systemd_unit_directory());
    assert!(
        is_mounted(their_ftps_jail.mount_point()),
        "the neighbour's FTPS bind mount must still be in place"
    );

    // Cleaned up here rather than left to the fixture, whose teardown is
    // deliberately narrower than the cascade (see `polygon_account.rs`).
    delete_the_account(&their_name);
}

/// The account the deletion/creation race runs against.
///
/// Its own name, so a failure here cannot be confused with the cascade's.
const RACE_ACCOUNT: &str = "polyracesftp";

/// The login that exists before the race, and through which the inverse
/// control proves an unraced creation really works on this host.
const KEEP_LOGIN: &str = "keep";

/// How many further logins the account is given before the race.
///
/// Not padding, and the number is measured rather than chosen. The window this
/// test has to enter runs from the moment the deletion revokes the account's
/// FIRST login to the moment it returns, and the SFTP step removes the logins
/// ONE AT A TIME — a `userdel` spawn each — before taking the jail down. With
/// nine logins the whole deletion was timed at 354ms with only 68ms of it left
/// after the poll observed the first revocation, and a 2ms poll around a
/// `getent` spawn does not reliably land inside 68ms: the test failed three
/// runs out of three that way, and a race test that loses its own race proves
/// nothing. Forty turns that residue into something a poll can enter without
/// the answer depending on how fast the machine running it happens to be.
/// An account with a few dozen file-transfer logins is also a real customer.
const FILLER_LOGINS: usize = 40;

/// The login the race tries to create while the deletion is running.
const RACING_LOGIN: &str = "racer";

/// The fully-qualified name of the login that exists before the race.
fn keep_login_of(account: &AccountName) -> String {
    format!("{}_{KEEP_LOGIN}", account.as_str())
}

/// How long the test waits for the deletion to reach its SFTP step.
const OVERLAP_TIMEOUT: std::time::Duration = std::time::Duration::from_secs(60);

/// How often it looks.
///
/// Short, because the poll's own latency comes out of the window it is trying
/// to enter: the window was MEASURED at this scale rather than assumed (see
/// [`FILLER_LOGINS`]), and a `getent` spawn per look already costs a few
/// milliseconds of it.
const OVERLAP_POLL: std::time::Duration = std::time::Duration::from_millis(2);

/// Whether the host's password database holds a login called `login`.
///
/// Asked of `getent`, the same database `remove_account_sftp` enumerates, and
/// not of a file path: where the database lives is a platform fact.
///
/// # Panics
///
/// Panics when `getent` cannot be run at all.
fn passwd_holds(login: &str) -> bool {
    Command::new(polygon_distro().getent_binary())
        .args(["passwd", login])
        .env("LC_ALL", "C")
        .stdout(std::process::Stdio::null())
        .status()
        .expect("the polygon image installs getent")
        .success()
}

/// Waits until `login` is gone from the password database, or gives up.
///
/// A poll with a timeout and never a sleep of a guessed length
/// (rules/testing.md "Determinism"). What it waits for is an observable side
/// effect of the deletion's SFTP step: once the FIRST of the account's logins
/// is gone the deletion has certainly taken the account's lock — it takes it as
/// its first statement and holds it until it returns — and it has certainly not
/// yet run `userdel`, which is the last, with [`FILLER_LOGINS`] more `userdel`
/// spawns, a jail teardown, a crontab removal and a pool sweep still between
/// them. That is the exact window the audit measured, entered on purpose rather
/// than hoped for.
///
/// `getent` and not a query to the database server: the probe has to be cheap,
/// because the probe's own latency comes out of the window it is trying to
/// enter. A `mariadb` client spawn per poll was measured letting the whole
/// deletion finish between two looks.
///
/// # Panics
///
/// Panics when the deletion never reached that step, because a race whose
/// second operation never started measures nothing.
fn wait_until_login_is_revoked(login: &str) {
    let deadline = std::time::Instant::now() + OVERLAP_TIMEOUT;
    while std::time::Instant::now() < deadline {
        if !passwd_holds(login) {
            return;
        }
        std::thread::sleep(OVERLAP_POLL);
    }

    panic!(
        "the deletion never revoked {login} within {OVERLAP_TIMEOUT:?}: the two operations \
         did not overlap, so this run measured nothing"
    );
}

#[test]
#[ignore = "creates and deletes a real account with real logins and a real bind mount: polygon only"]
fn a_login_creation_that_overlaps_the_accounts_deletion_leaves_no_orphan_login_or_jail() {
    // C-3, interleaving 2, driven rather than argued. `create_sftp_user` reads
    // the account's uid, then builds a jail, writes a mount unit, runs
    // `daemon-reload` and `enable --now`, and only THEN runs
    // `useradd --non-unique --uid`. A deletion inside that window revoked the
    // logins that existed at the time and ran `userdel`; the `useradd`
    // afterwards still succeeded, because `--non-unique` does not care that
    // the uid is now unassigned. What was left was a passwd entry, a jail, an
    // enabled mount unit and a password the customer had been shown — and
    // `remove_account_sftp` enumerates the password database at the moment it
    // runs, so nothing would ever look for them again. This host recycles
    // account names, so the next tenant of this name inherits the mount and
    // the old customer's password opens it.
    let server = PolygonMariadb::start();
    let sshd = PolygonSshd::start();
    let account = PolygonAccount::create(RACE_ACCOUNT);
    let name = account.name().clone();
    let jail = AccountJail::for_account(&name, polygon_distro().systemd_unit_directory());
    provision(&server, &name);

    let keep = SftpUserRequest {
        account: name.clone(),
        user: SftpUserName::for_account(&name, KEEP_LOGIN).expect("a valid login name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &keep).unwrap_or_else(|error| {
        panic!(
            "the first login must be creatable on this host: {error}. A JailFailed here \
             usually means the container was started without --privileged."
        )
    });
    for index in 0..FILLER_LOGINS {
        let filler = SftpUserRequest {
            account: name.clone(),
            user: SftpUserName::for_account(&name, &format!("fill{index}"))
                .expect("a valid login name"),
            password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
        };
        create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &filler)
            .unwrap_or_else(|error| panic!("login {index} must be creatable: {error}"));
    }

    // The inverse control, and it comes first: a login creation that races
    // NOTHING must still work. Without it, every assertion below would pass
    // just as well against an operation that had been made to refuse always.
    let session = sshd.sftp(&keep_login_of(&name), CUSTOMER_PASSWORD, "cd home\nls\n");
    assert!(
        session.status.success(),
        "an unraced login must work before the race:\n{}",
        said(&session)
    );

    let deleting = std::thread::spawn({
        let name = name.clone();
        move || {
            operations().delete(
                &ProcessPhpHost::new(),
                &ProcessDbHost::new(polygon_distro()),
                &ProcessSftpHost::new(),
                &ProcessFtpsHost::new(),
                &name,
            )
        }
    });

    // Entered deliberately: the deletion holds the account's lock and has not
    // yet run `userdel`.
    wait_until_login_is_revoked(&keep_login_of(&name));

    let racing = SftpUserRequest {
        account: name.clone(),
        user: SftpUserName::for_account(&name, RACING_LOGIN).expect("a valid login name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    let raced = create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &racing);

    let deleted = deleting.join().expect("the deletion thread must not panic");
    assert!(
        deleted.is_ok(),
        "the deletion must still succeed while a creation is refused: {deleted:?}"
    );

    // The corruption the audit named, asserted against the host itself and
    // asserted FIRST: it is the subject, and a reader of a failing run should
    // see the orphan rather than a refusal that did not happen.
    assert!(
        !passwd_holds(&format!("{}_{RACING_LOGIN}", name.as_str())),
        "an orphaned login survived a deletion that had already enumerated the \
         password database: the next tenant of this name inherits a live credential"
    );
    assert!(
        !Path::new(jail.unit_path()).exists(),
        "an orphaned mount unit survived: on the next boot it bind-mounts whichever \
         home holds this name into a jail nothing owns"
    );
    assert!(
        !Path::new(jail.directory()).exists(),
        "an orphaned jail directory survived"
    );

    // And the refusal, which is what proves the two really overlapped: a
    // creation that had run before or after the deletion would have answered
    // `AlreadyExists` or `AccountMissing`, and neither says anything about
    // exclusion.
    assert!(
        matches!(raced, Err(SftpError::AccountBusy)),
        "the creation must be refused as busy while the deletion holds the account's \
         lock, got {raced:?}"
    );

    assert!(
        !passwd_holds(name.as_str()),
        "the account itself must be gone: the deletion reported success"
    );
}

/// The account the pool-write/deletion race runs against.
///
/// Its own name, like every other case in this file: `take_account_lock` is a
/// process-wide registry keyed by the account name, and two cases sharing a
/// name would refuse each other rather than measure anything.
const POOL_RACE_ACCOUNT: &str = "polyracepool";

/// A worker budget inside the range `write_pool` accepts.
const POOL_RACE_WORKERS: u32 = 4;

/// The pool the race writes: this account, the polygon's PHP version, a modest
/// worker budget and no overrides.
fn pool_for(account: &AccountName) -> PoolInput {
    PoolInput {
        account: account.clone(),
        version: PolygonPhp::version(),
        max_children: POOL_RACE_WORKERS,
        overrides: Vec::new(),
    }
}

/// Waits until the deletion's pool sweep has removed `pool`, or gives up.
///
/// A poll with a timeout and never a sleep of a guessed length
/// (rules/testing.md "Determinism"), and the side effect it waits for is the
/// LAST one the deletion produces before `userdel`: `remove_account_pools` runs
/// immediately before the existence re-check and the removal itself. Once this
/// file is gone the deletion has certainly taken the account's lock — it takes
/// it as its first statement and holds it until it returns — and it has
/// certainly not yet run `userdel`, because it still owes the removal's own
/// `php-fpm -t`, its reload, five more versions and a `getent` spawn.
///
/// That is the window the audit describes, and entering it here rather than
/// earlier is what makes the mutation proof honest: a write fired at the
/// beginning of the deletion would simply be swept up by this very step, and
/// the run would look like a pass whatever the lock did.
///
/// A `stat` of one path per look, which is the cheapest probe in this file:
/// the probe's own latency comes out of the window it is trying to enter.
///
/// # Panics
///
/// Panics when the sweep never happened, because a race whose second operation
/// never started measures nothing.
fn wait_until_the_pool_is_swept(pool: &Path) {
    let deadline = std::time::Instant::now() + OVERLAP_TIMEOUT;
    while std::time::Instant::now() < deadline {
        if !pool.exists() {
            return;
        }
        std::thread::sleep(OVERLAP_POLL);
    }

    panic!(
        "the deletion never removed {pool:?} within {OVERLAP_TIMEOUT:?}: the two operations \
         did not overlap, so this run measured nothing"
    );
}

#[test]
#[ignore = "writes a real php-fpm pool, deletes a real account and runs the real php-fpm -t: polygon only"]
fn a_pool_write_that_overlaps_the_accounts_deletion_leaves_no_pool_naming_a_deleted_user() {
    // C-3, interleaving 1, driven rather than argued — and the one the deletion's
    // step order could only narrow. `write_pool` renders a pool, swaps it in and
    // validates it; a write that lands after `remove_account_pools` has run
    // leaves that file behind, `userdel` then takes the user it names away, and
    // the next `php-fpm -t` on this host — any tenant's, days later, for a
    // completely unrelated reason — answers `cannot get uid for user` and the
    // master refuses to start or reload AT ALL. That is the trap
    // `ops::php::remove_pool` exists to close, re-armed by an interleaving
    // instead of by a missing operation.
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create(POOL_RACE_ACCOUNT);
    let name = account.name().clone();
    provision(&server, &name);

    let paths = PoolPaths::for_pool(polygon_distro(), &name, &PolygonPhp::version());
    let pool_path = paths.config_path.clone();
    // Removed by the fixture whatever this test does, so a failing run leaves no
    // pool behind to poison every later run in this container.
    let _pool = PolygonConfigFile::at(&pool_path);

    // The inverse control, and it comes first: a pool write that races NOTHING
    // must succeed, and the tree it leaves must be one the real php-fpm accepts.
    // Without it every assertion below would pass just as well against an
    // operation that had been made to refuse always.
    write_pool(&ProcessPhpHost::new(), polygon_distro(), &pool_for(&name))
        .unwrap_or_else(|error| panic!("an unraced pool write must succeed: {error}"));
    assert!(
        pool_path.exists(),
        "the pool the race is about must be on disk at {pool_path:?} before it starts"
    );
    PolygonPhp::assert_tree_valid(polygon_distro(), "the pool written before the race");

    let deleting = std::thread::spawn({
        let name = name.clone();
        move || {
            operations().delete(
                &ProcessPhpHost::new(),
                &ProcessDbHost::new(polygon_distro()),
                &ProcessSftpHost::new(),
                &ProcessFtpsHost::new(),
                &name,
            )
        }
    });

    // Entered deliberately: the deletion holds the account's lock, has swept the
    // pools, and has not yet run `userdel`.
    wait_until_the_pool_is_swept(&pool_path);

    let raced = write_pool(&ProcessPhpHost::new(), polygon_distro(), &pool_for(&name));

    let deleted = deleting.join().expect("the deletion thread must not panic");
    assert!(
        deleted.is_ok(),
        "the deletion must still succeed while a pool write is refused: {deleted:?}"
    );

    // The corruption the audit named, asserted against the host itself and
    // asserted FIRST: it is the subject, and a reader of a failing run should
    // see the surviving pool rather than a refusal that did not happen.
    assert!(
        !pool_path.exists(),
        "a php-fpm pool naming a user that no longer exists survived the deletion at \
         {pool_path:?}: the next php-fpm reload on this host — any tenant's, days later — \
         takes PHP down for every account on the machine. The real php-fpm -t already \
         says, of this host's whole pool tree: {}",
        PolygonPhp::tree_refusal(polygon_distro())
            .unwrap_or_else(|| "it still accepts the tree".to_owned())
    );

    // And the consequence, which is what makes this HOST-WIDE rather than one
    // account's problem: the real validator, over the whole pool directory,
    // after the account is gone.
    PolygonPhp::assert_tree_valid(
        polygon_distro(),
        "the pool tree left behind by a write that raced this account's deletion",
    );

    // The refusal, which is what proves the two really overlapped: a write that
    // had run entirely before the deletion would have answered `Ok` and one that
    // ran entirely after it would have failed its own `php-fpm -t` on a user
    // that no longer resolves, and neither says anything about exclusion.
    match raced {
        Err(PhpOpError::AccountBusy { username }) => assert_eq!(username, name.as_str()),
        other => panic!(
            "the pool write must be refused as busy while the deletion holds the \
             account's lock, got {other:?}"
        ),
    }

    assert!(
        !passwd_holds(name.as_str()),
        "the account itself must be gone: the deletion reported success"
    );
}

/// The account the FTPS deletion/creation race runs against.
///
/// Its own name, so a failure here cannot be confused with the SFTP race's.
const FTPS_RACE_ACCOUNT: &str = "polyraceftps";

/// The FTPS login created inside the deletion's window.
const RACING_FTPS_LOGIN: &str = "racer";

#[test]
#[ignore = "creates and deletes a real account with real logins and two real bind mounts: polygon only"]
fn an_ftps_login_creation_that_overlaps_the_accounts_deletion_leaves_no_orphan_login_or_jail() {
    // The FTPS half of the exclusion, driven rather than argued, and driven for
    // the reason the SFTP half is: `create_ftps_user` reads the account's uid,
    // builds a jail, writes and enables a mount unit, and only THEN runs
    // `useradd --non-unique --uid`. A deletion inside that window revoked the
    // logins that existed when it enumerated them and ran `userdel`; the
    // `useradd` afterwards would still succeed, because `--non-unique` does not
    // care that the uid is now unassigned. What would be left is a passwd entry
    // in the FTPS group, a jail, an enabled mount unit and a password the
    // customer had been shown — and `remove_account_ftps` enumerates the
    // password database at the moment it runs, so nothing would look for them
    // again.
    //
    // Two things are asserted in the window and both matter: that the creation
    // is REFUSED (the exclusion, observed rather than inferred from the absence
    // of an orphan), and that a second DELETION is refused too — which is the
    // same lock read from the other side, and the direction a caller sees when
    // an operator clicks delete twice.
    // No sshd fixture: nothing here logs in over SFTP. The SFTP logins below
    // exist only to make the deletion's own window wide enough to enter, and
    // this test's subject is the FTPS half of the cascade.
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create(FTPS_RACE_ACCOUNT);
    let name = account.name().clone();
    let ftps_jail = FtpsJail::for_account(&name, polygon_distro().systemd_unit_directory());
    provision(&server, &name);

    // The inverse control, and it comes first: an FTPS login creation that races
    // NOTHING must work, and its jail must really carry the account's home.
    // Without this half, every assertion below would pass just as well against
    // an operation that had been made to refuse always.
    assert!(
        passwd_holds(&ftps_login_of(&name)),
        "an unraced FTPS login must be creatable on this host"
    );
    assert!(
        is_mounted(ftps_jail.mount_point()),
        "an unraced FTPS jail must really be mounted. A failure here usually means \
         the container was started without --privileged."
    );

    // The window is entered through the SFTP logins, because the deletion
    // revokes those first and one at a time: by the moment the first is gone the
    // deletion holds the account's lock and still has the rest of its logins,
    // the FTPS step, the crontab and the pool sweep to run.
    for index in 0..FILLER_LOGINS {
        let filler = SftpUserRequest {
            account: name.clone(),
            user: SftpUserName::for_account(&name, &format!("fill{index}"))
                .expect("a valid login name"),
            password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
        };
        create_sftp_user(&ProcessSftpHost::new(), polygon_distro(), &filler)
            .unwrap_or_else(|error| panic!("login {index} must be creatable: {error}"));
    }

    let deleting = std::thread::spawn({
        let name = name.clone();
        move || {
            operations().delete(
                &ProcessPhpHost::new(),
                &ProcessDbHost::new(polygon_distro()),
                &ProcessSftpHost::new(),
                &ProcessFtpsHost::new(),
                &name,
            )
        }
    });

    wait_until_login_is_revoked(&sftp_login_of(&name));

    let racing = FtpsUserRequest {
        account: name.clone(),
        user: FtpsUserName::for_account(&name, RACING_FTPS_LOGIN).expect("a valid login name"),
        password: Password::parse(CUSTOMER_PASSWORD).expect("a valid password"),
    };
    let raced = create_ftps_user(&ProcessFtpsHost::new(), polygon_distro(), &racing);
    let second_deletion = operations().delete(
        &ProcessPhpHost::new(),
        &ProcessDbHost::new(polygon_distro()),
        &ProcessSftpHost::new(),
        &ProcessFtpsHost::new(),
        &name,
    );

    let deleted = deleting.join().expect("the deletion thread must not panic");
    assert!(
        deleted.is_ok(),
        "the deletion must still succeed while a creation is refused: {deleted:?}"
    );

    // The corruption first: it is the subject, and a reader of a failing run
    // should see the orphan rather than a refusal that did not happen.
    assert!(
        !passwd_holds(&format!("{}_{RACING_FTPS_LOGIN}", name.as_str())),
        "an orphaned FTPS login survived a deletion that had already enumerated \
         the password database: the next tenant of this name inherits a live \
         credential"
    );
    assert!(
        !Path::new(ftps_jail.unit_path()).exists(),
        "an orphaned FTPS mount unit survived: on the next boot it bind-mounts \
         whichever home holds this name into a jail nothing owns"
    );
    assert!(
        !Path::new(ftps_jail.directory()).exists(),
        "an orphaned FTPS jail survived the account it belonged to"
    );
    assert!(
        !is_mounted(ftps_jail.mount_point()),
        "an orphaned FTPS bind mount survived the home it pointed at"
    );

    // The refusals, which are what prove the two really overlapped: a creation
    // that had run entirely before the deletion would have answered `Ok`, and
    // one that ran entirely after it would have answered `AccountMissing` —
    // neither says anything about exclusion.
    match raced {
        Err(FtpsError::AccountBusy) => {}
        other => panic!(
            "the FTPS login creation must be refused as busy while the deletion \
             holds the account's lock, got {other:?}"
        ),
    }
    match second_deletion {
        Err(AccountError::Busy { username }) => assert_eq!(username, name.as_str()),
        other => panic!(
            "a second deletion must be refused while the first holds the account's \
             lock, got {other:?}"
        ),
    }

    assert!(
        !passwd_holds(name.as_str()),
        "the account itself must be gone: the deletion reported success"
    );
}
