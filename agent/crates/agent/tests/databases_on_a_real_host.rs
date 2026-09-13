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
//!   with the ORIGINAL password after a second create asked for a different one;
//! - that the grant's database name is a PATTERN and not an identifier, which is
//!   a fact about the server's grammar that no fake can hold an opinion about.
//!   `_` matches any single character there and backtick-quoting does not turn it
//!   off, so `polydbgrant_ne_shop` unescaped reaches `polydbgrantone_shop`. That
//!   is
//!   `a_grant_for_an_account_whose_name_holds_the_separator_cannot_reach_a_same_length_neighbours_database`,
//!   and the two account names it uses are a matched pair whose GEOMETRY is
//!   asserted before anything is created — see
//!   `the_geometry_that_makes_a_collision_possible`. The case directly above it
//!   asks the same question of two names of DIFFERENT lengths, where a single-`_`
//!   pattern cannot reach, so it passed for the whole life of the defect.

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

/// The victim account of the colliding-geometry case below.
///
/// **This name and [`COLLIDING_ATTACKER_ACCOUNT`] are a matched pair, and the
/// test is worthless if the pair is broken.** Do not rename either one without
/// reading `the_geometry_that_makes_a_collision_possible` below, which asserts
/// the relationship between them and fails loudly rather than quietly passing.
const COLLIDING_VICTIM_ACCOUNT: &str = "polydbgrantone";

/// The attacker account, whose name is the victim's with one character replaced
/// by the separator.
///
/// `polydbgrant_ne` against `polydbgrantone`: the same fourteen characters long,
/// differing only at index eleven, where this one holds `_`.
const COLLIDING_ATTACKER_ACCOUNT: &str = "polydbgrant_ne";

/// The row planted in the victim's database, which no grant names the attacker on.
const VICTIM_SECRET: &str = "VICTIM-CARD-4111111111111111";

/// Asserts that the two account names above can actually collide under a
/// single-`_` pattern, and says in full why that is the whole test.
///
/// **This is the guard against the defect that hid the original bug through four
/// reviews, and it is the reason a renamed fixture can no longer destroy this
/// case silently.** In MySQL and MariaDB the database name of a database-level
/// `GRANT` is a LIKE-style pattern, where `_` matches any **single** character.
/// A pattern whose only metacharacter is `_` therefore matches only strings of
/// **its own length**. So two account names of different lengths cannot collide
/// whatever the server does, and a test built on such a pair passes identically
/// whether the grant is escaped or not.
///
/// That is exactly what happened to
/// `the_created_db_user_can_connect_with_the_generated_password_and_see_only_its_own_database`
/// above: `polydbstwo_shop` is fifteen characters and `polydbsthree_shop` is
/// seventeen, so its real assertion against a real server could not have failed
/// on either side of the behaviour it claimed to distinguish. Measured on
/// MariaDB 10.11.14: an unescaped grant on the fifteen-character pattern is
/// refused on the seventeen-character name.
///
/// The three conditions below are therefore the test's premise, not decoration.
/// Should somebody rename either constant — for tidiness, for a suffix clash,
/// for a length limit — and break any of them, this assertion fails by name and
/// says what was lost, instead of the case going green over a geometry in which
/// the wildcard is invisible.
///
/// # Panics
///
/// Panics when the two names can no longer collide under a single-`_` pattern.
fn the_geometry_that_makes_a_collision_possible() {
    let victim = COLLIDING_VICTIM_ACCOUNT;
    let attacker = COLLIDING_ATTACKER_ACCOUNT;

    assert_eq!(
        victim.len(),
        attacker.len(),
        "the two account names must be the SAME LENGTH ({victim} is {} and \
         {attacker} is {}). A `_` in a GRANT pattern matches exactly one \
         character, so names of different lengths cannot collide and this test \
         would pass with the defect present — which is how the original bug \
         survived four reviews.",
        victim.len(),
        attacker.len()
    );

    let differences: Vec<usize> = victim
        .bytes()
        .zip(attacker.bytes())
        .enumerate()
        .filter(|(_, (v, a))| v != a)
        .map(|(index, _)| index)
        .collect();
    assert_eq!(
        differences.len(),
        1,
        "the two account names must differ at EXACTLY ONE position, and they \
         differ at {differences:?}. Every differing position needs its own `_` \
         in the attacker's name to be covered, so a second difference that is \
         not an underscore makes the collision impossible."
    );

    let position = differences[0];
    assert_eq!(
        attacker.as_bytes()[position],
        b'_',
        "at the one differing position ({position}) the attacker's name must \
         hold the separator `_`, which is the character the server reads as a \
         wildcard. It holds {:?}.",
        attacker.as_bytes()[position] as char
    );
    assert_ne!(
        victim.as_bytes()[position],
        b'_',
        "the victim must hold a literal character where the attacker holds the \
         wildcard, or the two names are the same name and there is no boundary \
         to cross."
    );
}

#[test]
#[ignore = "creates two colliding real accounts and real databases on a real MariaDB: polygon only"]
fn a_grant_for_an_account_whose_name_holds_the_separator_cannot_reach_a_same_length_neighbours_database()
 {
    // The premise first, before a single system account is made: if the two
    // names cannot collide, nothing below this line measures anything.
    the_geometry_that_makes_a_collision_possible();

    let server = PolygonMariadb::start();
    let victim = PolygonAccount::create(COLLIDING_VICTIM_ACCOUNT);
    let attacker = PolygonAccount::create(COLLIDING_ATTACKER_ACCOUNT);
    clear(&server, victim.name());
    clear(&server, attacker.name());

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(victim.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the victim's database must be created: {error}"));
    create_database(&host, &request_for(attacker.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the attacker's database must be created: {error}"));

    let victim_login = format!("{}_shop", victim.name().as_str());
    let attacker_login = format!("{}_shop", attacker.name().as_str());
    let victim_database = victim_login.clone();
    let attacker_database = attacker_login.clone();

    // A real secret in the victim's database, so the refusal below is about
    // DATA and not about a table that happens not to exist. Planted by root,
    // which no grant is involved in.
    server.run(&format!(
        "CREATE TABLE `{victim_database}`.customers (id INT, secret VARCHAR(64))"
    ));
    server.run(&format!(
        "INSERT INTO `{victim_database}`.customers VALUES (1, '{VICTIM_SECRET}')"
    ));

    // POSITIVE CONTROL for the probe itself, on the axis that can go blind: the
    // statement the attacker will be refused must be one that SUCCEEDS and
    // returns the secret for somebody. Run as the victim's own user, it does.
    // Without this, a mistyped table name, an empty table or a database that was
    // never created would produce the same refusal and prove nothing at all.
    let owner_reads = server.run_as(
        &victim_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT secret FROM `{victim_database}`.customers"),
    );
    assert!(
        owner_reads.status.success(),
        "the victim's own user must be able to read the planted row, or the \
         refusal measured below is not about access:\n{}",
        String::from_utf8_lossy(&owner_reads.stderr)
    );
    assert!(
        String::from_utf8_lossy(&owner_reads.stdout).contains(VICTIM_SECRET),
        "the probe must be able to see the secret when it is allowed to, or it \
         could not see it when it is not"
    );

    // 1. THE FINDING. With the grant written unescaped this SELECT succeeds and
    //    returns the victim's row: `polydbgrant_ne_shop` as a pattern is
    //    `polydbgrant?ne?shop`, which matches `polydbgrantone_shop` character
    //    for character. Measured on MariaDB 10.11.14 with the escape removed.
    let refused = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT secret FROM `{victim_database}`.customers"),
    );
    assert!(
        !refused.status.success(),
        "the attacker's user reached the victim's database. The GRANT's database \
         name is a LIKE pattern, so an unescaped `_` in it matches any single \
         character; this is a cross-tenant read of another customer's data."
    );
    let complaint = String::from_utf8_lossy(&refused.stderr);
    assert!(
        complaint.contains("denied"),
        "the refusal must be the server DENYING access rather than any other \
         failure — a missing table or an unstarted server would refuse too, and \
         would prove nothing:\n{complaint}"
    );
    assert!(
        !String::from_utf8_lossy(&refused.stdout).contains(VICTIM_SECRET),
        "the victim's row must not appear in the attacker's output"
    );

    // 2. Nor may the attacker write, which is the half that destroys data rather
    //    than reading it. A pattern grant is ALL PRIVILEGES, `DROP TABLE`
    //    included.
    let written = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("INSERT INTO `{victim_database}`.customers VALUES (99, 'attacker')"),
    );
    assert!(
        !written.status.success(),
        "the attacker must not be able to write into the victim's table"
    );

    // 3. Nor see that the victim exists at all.
    let visible = server.run_as(&attacker_login, CUSTOMER_PASSWORD, "SHOW DATABASES");
    let names = String::from_utf8_lossy(&visible.stdout);
    assert!(
        !names.lines().any(|line| line.trim() == victim_database),
        "the attacker must not see the victim's database:\n{names}"
    );

    // 4. INVERSE CONTROL, and it is mandatory rather than thorough: the escape
    //    must scope the grant, not destroy it. An escaped name that matched
    //    NOTHING would pass every assertion above while breaking every
    //    customer's own application — a worse defect than the one being fixed.
    let own = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("CREATE TABLE `{attacker_database}`.orders (id INT)"),
    );
    assert!(
        own.status.success(),
        "the attacker's user must still hold full privileges on its OWN \
         database — an escape that also broke the legitimate grant would be the \
         worse defect:\n{}",
        String::from_utf8_lossy(&own.stderr)
    );
    let own_insert = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("INSERT INTO `{attacker_database}`.orders VALUES (5)"),
    );
    assert!(
        own_insert.status.success(),
        "the owner must be able to write its own data:\n{}",
        String::from_utf8_lossy(&own_insert.stderr)
    );
    assert!(
        names.lines().any(|line| line.trim() == attacker_database),
        "the owner must see its own database:\n{names}"
    );

    // 5. And the reach is not only into databases that already exist: grants are
    //    stored as patterns in `mysql.db` and matched when a connection asks for
    //    a database, so a database created AFTER the grant is inside an
    //    unescaped pattern's reach. Another same-length name matching the
    //    attacker's pattern, made now.
    let later = format!("{}xne_shop", &COLLIDING_VICTIM_ACCOUNT[..11]);
    server.run(&format!("DROP DATABASE IF EXISTS `{later}`"));
    server.run(&format!("CREATE DATABASE `{later}`"));
    server.run(&format!("CREATE TABLE `{later}`.later (id INT)"));
    let future = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT COUNT(*) FROM `{later}`.later"),
    );
    assert!(
        !future.status.success(),
        "a database created after the grant must be out of reach too: patterns \
         are matched at connection time, so an unescaped grant claims names that \
         did not exist when it was issued"
    );
    server.run(&format!("DROP DATABASE IF EXISTS `{later}`"));

    clear(&server, victim.name());
    clear(&server, attacker.name());
}
