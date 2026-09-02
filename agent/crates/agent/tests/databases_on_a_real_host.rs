//! Databases against a real MariaDB, which is the only place `ops::db` means
//! anything.
//!
//! Every test of that area so far has run against a `DbHost` that returned
//! whatever the test wanted, so the operations have been proved to react
//! correctly to answers nobody has ever asked a server for. This suite asks a
//! server — the one the polygon images install from the INSTALLER's own package
//! list, reached the way the agent reaches it: over the local socket, as root,
//! with no credential of any kind.
//!
//! What only a real server can settle, and what each test is here for:
//!
//! - that the statements the agent builds are ones this server accepts at all,
//!   including the character set and collation it names;
//! - that the password reached the server intact. A fake cannot tell a password
//!   that survived from one that was truncated, mangled or quoted into
//!   something else, because a fake never authenticates anybody. Here the
//!   created user logs in with it;
//! - that the grant is SCOPED. `GRANT ALL PRIVILEGES ON \`db\`.*` is a string
//!   until a server parses it; the test that matters is another tenant's
//!   database being refused to the user this one created;
//! - that a repeat leaves the existing credential alone, checked by logging in
//!   with the ORIGINAL password after a second create asked for a different one.

// A failing assertion IS the reporting mechanism for a test, so the
// workspace-wide bans on unwrap/expect/panic are lifted here only.
#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

// The shared account fixture carries more than any one suite uses — this one
// never asks for an account's ids or home — and an unused field there is not a
// defect in it.
#[allow(dead_code)]
#[path = "fixtures/polygon_account.rs"]
mod polygon_account;
#[path = "fixtures/polygon_mariadb.rs"]
mod polygon_mariadb;

use std::path::Path;

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;
use maran_distro::{DistroAdapter, adapter_for, detect};
use maran_ops::db::{
    CreateDatabaseRequest, DbError, ProcessDbHost, create_database, database_size, drop_database,
    list_databases, set_database_password,
};

use polygon_account::PolygonAccount;
use polygon_mariadb::PolygonMariadb;

/// The password every login in this suite is created with.
///
/// It uses every character class `Password` allows — letters, digits and
/// `-_.=+` — on purpose. A password that reached the server mangled would fail
/// to authenticate, and a password made only of letters would not notice a
/// server or a quoting bug that ate the punctuation.
const CUSTOMER_PASSWORD: &str = "Str0ng-pass.word=+_";

/// The password a REPEATED create asks for, and which must not take effect.
const SECOND_PASSWORD: &str = "Different-2.password";

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

/// A create request for `account`'s `shop` database and `shop` user.
fn request_for(account: &AccountName, password: &str) -> CreateDatabaseRequest {
    CreateDatabaseRequest {
        database: DatabaseName::for_account(account, "shop").expect("a valid database name"),
        user: DbUserName::for_account(account, "shop").expect("a valid user name"),
        password: Password::parse(password).expect("a valid password"),
    }
}

/// Removes anything a previous run of this suite left behind under `account`.
///
/// Removal rather than reuse: a test that starts from another run's state
/// proves nothing about the operation it is exercising. It goes through the
/// client directly rather than through `drop_database`, because the code under
/// test must not be what prepares the ground for the test.
fn clear(server: &PolygonMariadb, account: &AccountName) {
    let name = account.as_str();
    server.run(&format!("DROP DATABASE IF EXISTS `{name}_shop`"));
    server.run(&format!("DROP USER IF EXISTS '{name}_shop'@'localhost'"));
}

#[test]
#[ignore = "creates a real database on a real MariaDB: polygon only"]
fn the_mysql_client_the_adapter_names_exists_on_this_family() {
    PolygonMariadb::require_polygon();

    // The cheapest test in the suite and the one that would catch a whole
    // family's databases being unusable. `ProcessDbHost` execs this exact path,
    // so a path that is merely plausible turns every database operation on that
    // family into "the client could not be started" — which an operator sees as
    // ClientFailed { code: -1 } with nothing to fix.
    let client = polygon_distro().mysql_client_binary();
    assert!(
        Path::new(client).exists(),
        "the adapter names {client} as this family's client and it is not there"
    );
}

#[test]
#[ignore = "creates a real database on a real MariaDB: polygon only"]
fn a_database_created_by_the_agent_is_visible_to_the_real_mysql_client() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polydbsone");
    clear(&server, account.name());

    create_database(
        &ProcessDbHost::new(polygon_distro()),
        &request_for(account.name(), CUSTOMER_PASSWORD),
    )
    .unwrap_or_else(|error| panic!("creating a database must succeed in the polygon: {error}"));

    // Asked of the server by a client this suite spawned, not of the code that
    // did the work: `list_databases` reading back its own effect would pass on
    // a create that never reached MariaDB.
    let listing = server.run("SHOW DATABASES");
    let names = String::from_utf8_lossy(&listing.stdout);
    let expected = format!("{}_shop", account.name().as_str());
    assert!(
        names.lines().any(|line| line.trim() == expected),
        "the server must hold {expected}, it holds:\n{names}"
    );

    // The character set is part of what the agent decided, and getting it wrong
    // truncates a customer's rows at the first four-byte character rather than
    // failing anything. The server is asked what it actually recorded.
    let charset = server.run(&format!(
        "SELECT default_character_set_name FROM information_schema.schemata \
         WHERE schema_name = '{expected}'"
    ));
    assert_eq!(
        String::from_utf8_lossy(&charset.stdout).trim(),
        "utf8mb4",
        "a database created with the server's default encoding silently \
         truncates rows at the first four-byte character"
    );

    // And the agent's own two read paths against the same real server.
    let host = ProcessDbHost::new(polygon_distro());
    let owned = list_databases(&host, account.name()).expect("the listing must succeed");
    assert_eq!(
        owned
            .iter()
            .map(|row| row.name.as_str())
            .collect::<Vec<_>>(),
        vec![expected.as_str()],
        "the account's own database, and nothing belonging to anybody else"
    );

    let size = database_size(
        &host,
        &DatabaseName::for_account(account.name(), "shop").expect("valid"),
    )
    .expect("measuring an empty database must succeed");
    assert_eq!(
        size.bytes, 0,
        "an empty database is nought bytes, not an unparsable answer"
    );

    clear(&server, account.name());
}

#[test]
#[ignore = "connects with the created credentials: polygon only"]
fn the_created_db_user_can_connect_with_the_generated_password_and_see_only_its_own_database() {
    let server = PolygonMariadb::start();
    let mine = PolygonAccount::create("polydbstwo");
    let neighbour = PolygonAccount::create("polydbsthree");
    clear(&server, mine.name());
    clear(&server, neighbour.name());

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(mine.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("creating my database must succeed: {error}"));
    create_database(&host, &request_for(neighbour.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("creating the neighbour's database must succeed: {error}"));

    let login = format!("{}_shop", mine.name().as_str());
    let mine_database = format!("{}_shop", mine.name().as_str());
    let neighbour_database = format!("{}_shop", neighbour.name().as_str());

    // 1. The password survived. Nothing else in the project can establish this:
    //    the agent interpolates it into `IDENTIFIED BY '…'` and never reads it
    //    back, so a truncation or a quoting fault would show up here and only
    //    here, as a login that does not work.
    let connected = server.run_as(&login, CUSTOMER_PASSWORD, "SELECT 1");
    assert!(
        connected.status.success(),
        "the created user must connect with the password the agent was given:\n{}",
        String::from_utf8_lossy(&connected.stderr)
    );

    // 2. It can really use its own database, not merely authenticate.
    let owned = server.run_as(
        &login,
        CUSTOMER_PASSWORD,
        &format!("CREATE TABLE `{mine_database}`.orders (id INT)"),
    );
    assert!(
        owned.status.success(),
        "the user must have full privileges on its own database:\n{}",
        String::from_utf8_lossy(&owned.stderr)
    );

    // 3. The grant is scoped. This is the assertion the whole area rests on: a
    //    `GRANT ALL PRIVILEGES ON *.*` would pass every check above and hand one
    //    customer every other customer's data on the host.
    let refused = server.run_as(
        &login,
        CUSTOMER_PASSWORD,
        &format!("SELECT COUNT(*) FROM `{neighbour_database}`.anything"),
    );
    assert!(
        !refused.status.success(),
        "the user must not reach another account's database"
    );
    let complaint = String::from_utf8_lossy(&refused.stderr);
    assert!(
        complaint.contains("denied"),
        "the refusal must be the server denying access rather than any other \
         failure — a missing table would refuse too, and would prove nothing:\n{complaint}"
    );

    // 4. And it cannot even see that the neighbour exists.
    let visible = server.run_as(&login, CUSTOMER_PASSWORD, "SHOW DATABASES");
    let names = String::from_utf8_lossy(&visible.stdout);
    assert!(
        names.lines().any(|line| line.trim() == mine_database),
        "the user must see its own database:\n{names}"
    );
    assert!(
        !names.lines().any(|line| line.trim() == neighbour_database),
        "the user must not see another account's database:\n{names}"
    );

    // 5. A wrong password is refused, so the check above is a real credential
    //    check and not a server that lets anybody in.
    let wrong = server.run_as(&login, "Wr0ng-password", "SELECT 1");
    assert!(
        !wrong.status.success(),
        "the server must refuse a password that is not the one that was set"
    );

    clear(&server, mine.name());
    clear(&server, neighbour.name());
}

#[test]
#[ignore = "creates a real database on a real MariaDB: polygon only"]
fn a_repeated_create_reports_already_exists_and_leaves_the_first_password_working() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polydbsfour");
    clear(&server, account.name());

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(account.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the first create must succeed: {error}"));

    let repeated = create_database(&host, &request_for(account.name(), SECOND_PASSWORD));
    assert!(
        matches!(repeated, Err(DbError::AlreadyExists)),
        "a repeat must converge rather than fail, got {repeated:?}"
    );

    // The point of the idempotency rule, checked against the server rather than
    // against the agent's intention: the caller cannot tell a lost response from
    // a lost request, so it retries — and a retry that reset the password would
    // invalidate the credential the customer was already shown.
    let login = format!("{}_shop", account.name().as_str());
    let original = server.run_as(&login, CUSTOMER_PASSWORD, "SELECT 1");
    assert!(
        original.status.success(),
        "the first password must still work after a repeated create:\n{}",
        String::from_utf8_lossy(&original.stderr)
    );

    let second = server.run_as(&login, SECOND_PASSWORD, "SELECT 1");
    assert!(
        !second.status.success(),
        "the repeat's password must never have been set"
    );

    clear(&server, account.name());
}

#[test]
#[ignore = "drops a real database on a real MariaDB: polygon only"]
fn dropping_a_database_takes_its_user_with_it_and_a_second_drop_reports_not_found() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polydbsfive");
    clear(&server, account.name());

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(account.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the create must succeed: {error}"));

    let database = DatabaseName::for_account(account.name(), "shop").expect("valid");
    let user = DbUserName::for_account(account.name(), "shop").expect("valid");
    drop_database(&host, &database, &user)
        .unwrap_or_else(|error| panic!("the drop must succeed: {error}"));

    let listing = server.run("SHOW DATABASES");
    let expected = format!("{}_shop", account.name().as_str());
    assert!(
        !String::from_utf8_lossy(&listing.stdout)
            .lines()
            .any(|line| line.trim() == expected),
        "the database must be gone from the server"
    );

    // The credential goes with it. A database dropped while its user survives is
    // a live login to a server, belonging to a customer who has been told the
    // database is deleted.
    let survivors = server.run(&format!(
        "SELECT COUNT(*) FROM mysql.user WHERE User = '{expected}'"
    ));
    assert_eq!(
        String::from_utf8_lossy(&survivors.stdout).trim(),
        "0",
        "the dedicated user must be dropped with its database"
    );

    let again = drop_database(&host, &database, &user);
    assert!(
        matches!(again, Err(DbError::NotFound)),
        "a second drop must converge on NotFound, got {again:?}"
    );
}

#[test]
#[ignore = "resets a real password on a real MariaDB: polygon only"]
fn a_reset_password_authenticates_and_the_previous_one_stops_working() {
    let server = PolygonMariadb::start();
    let account = PolygonAccount::create("polydbssix");
    clear(&server, account.name());

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(account.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the create must succeed: {error}"));

    let user = DbUserName::for_account(account.name(), "shop").expect("valid");
    let login = format!("{}_shop", account.name().as_str());
    let replacement = Password::parse(SECOND_PASSWORD).expect("a valid password");

    set_database_password(&host, &user, &replacement)
        .unwrap_or_else(|error| panic!("resetting the password must succeed: {error}"));

    // 1. The new password reached the server intact. Nothing but a real
    //    authentication can establish this: the agent writes it into
    //    `ALTER USER … IDENTIFIED BY '…'` and never reads it back, so a
    //    truncation or a quoting fault shows up here and nowhere else.
    let renewed = server.run_as(&login, SECOND_PASSWORD, "SELECT 1");
    assert!(
        renewed.status.success(),
        "the user must connect with the password the reset was given:\n{}",
        String::from_utf8_lossy(&renewed.stderr)
    );

    // 2. And the OLD one is refused BY THE SERVER. A reset that adds a
    //    credential instead of replacing it looks identical to a working reset
    //    from the panel's side, and leaves the value the customer asked to
    //    revoke live on the host.
    let previous = server.run_as(&login, CUSTOMER_PASSWORD, "SELECT 1");
    assert!(
        !previous.status.success(),
        "the password that was replaced must no longer authenticate"
    );

    // 3. The reset changed the credential and nothing else: the grant is still
    //    the scoped one the create made, so a customer who resets a password
    //    does not lose access to their own data.
    let still_owned = server.run_as(
        &login,
        SECOND_PASSWORD,
        &format!("CREATE TABLE `{login}`.after_reset (id INT)"),
    );
    assert!(
        still_owned.status.success(),
        "the reset must leave the user's privileges on its own database:\n{}",
        String::from_utf8_lossy(&still_owned.stderr)
    );

    // 4. Repeating the reset succeeds rather than failing, and the value stands.
    //    A retry after a lost response is the ordinary way to reach this, and it
    //    must converge.
    set_database_password(&host, &user, &replacement)
        .unwrap_or_else(|error| panic!("a repeated reset must succeed: {error}"));
    let again = server.run_as(&login, SECOND_PASSWORD, "SELECT 1");
    assert!(
        again.status.success(),
        "a repeated reset must leave the same password working:\n{}",
        String::from_utf8_lossy(&again.stderr)
    );

    // 5. Resetting a user the server does not have is NotFound, not a silent
    //    success: the panel would otherwise show a customer a credential no
    //    server has ever heard of.
    let absent = DbUserName::for_account(account.name(), "nosuch").expect("valid");
    let missing = set_database_password(&host, &absent, &replacement);
    assert!(
        matches!(missing, Err(DbError::NotFound)),
        "resetting an absent user must report NotFound, got {missing:?}"
    );

    clear(&server, account.name());
}
