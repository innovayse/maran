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
//! - that a grant an OLDER agent issued as a pattern can be rewritten on a live
//!   server without anybody losing access, which is the only place the repair
//!   means anything either. The pre-fix state is SEEDED with the client rather
//!   than produced by the code under test, a positive control proves the seeded
//!   grant really does reach the victim's data before the repair runs, and a
//!   grant the panel did not issue is planted beside it and asserted untouched.
//!   That is
//!   `the_repair_narrows_a_wildcard_grant_an_older_agent_issued_and_touches_nothing_else`,
//!   on its own fifteen-character colliding pair.

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
    CreateDatabaseRequest, DbError, GrantRepairRefusal, ProcessDbHost, create_database,
    database_size, drop_database, list_databases, repair_grants, set_database_password,
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

/// Asserts that `victim` and `attacker` can actually collide under a single-`_`
/// pattern, and says in full why that is the whole test.
///
/// Parameterised over the pair rather than reading the constants directly,
/// because a SECOND case below — the repair of a grant an older agent issued —
/// needs its own accounts (two tests in one binary run in parallel and cannot
/// share a system account) and the same premise. One body, two pairs; a copy of
/// these three assertions would be a second answer to what a collision is.
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
fn the_geometry_that_makes_a_collision_possible(victim: &str, attacker: &str) {
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
    the_geometry_that_makes_a_collision_possible(
        COLLIDING_VICTIM_ACCOUNT,
        COLLIDING_ATTACKER_ACCOUNT,
    );

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

/// The victim account of the repair case below.
///
/// **A second matched pair, and a second geometry.** `polydbrepairone` and
/// [`REPAIR_ATTACKER_ACCOUNT`] are both FIFTEEN characters and differ at exactly
/// one position — index twelve — where the attacker holds `_`. It is a separate
/// pair from the collision case's fourteen-character one because two tests in one
/// binary run in parallel and cannot share a system account, and the premise is
/// asserted for this pair in its own right by
/// `the_geometry_that_makes_a_collision_possible`, before anything is created.
const REPAIR_VICTIM_ACCOUNT: &str = "polydbrepairone";

/// The attacker account of the repair case, holding the separator where the
/// victim holds a literal `o`.
const REPAIR_ATTACKER_ACCOUNT: &str = "polydbrepair_ne";

/// A database nothing in this panel created, granted with an unescaped pattern.
///
/// It decodes as a database name this agent COULD have created — account
/// `polyrepair`, suffix `foreign` — on purpose. Its user does not, so the pair
/// does not, and that is the axis the repair must refuse on: a row that merely
/// looks familiar is not one this panel issued.
const FOREIGN_DATABASE: &str = "polyrepair_foreign";

/// The user of that foreign grant.
///
/// It carries a separator and decodes perfectly well **on its own** — account
/// `reporting`, suffix `tool` — which is the point: what refuses this row is that
/// the user's account is not the DATABASE's account, and `create_database` only
/// ever pairs an account's own database with that account's own user. A fixture
/// whose user simply had no separator would be refused by a weaker predicate too,
/// and would not notice a classifier that read each half against its own name.
const FOREIGN_USER: &str = "reporting_tool";

/// The password the foreign user is created with.
const FOREIGN_PASSWORD: &str = "Foreign-1.password";

/// The `Db` column of every grant the server holds for `user`.
///
/// Read with the client directly rather than through the code under test, and
/// unescaped by hand: `--batch` prints a stored `a\_b` as `a\\_b`.
fn stored_patterns(server: &PolygonMariadb, user: &str) -> Vec<String> {
    let printed = server.run(&format!(
        "SELECT Db FROM mysql.db WHERE User = '{user}' ORDER BY Db"
    ));
    assert!(
        printed.status.success(),
        "the grant table must be readable:\n{}",
        String::from_utf8_lossy(&printed.stderr)
    );

    String::from_utf8_lossy(&printed.stdout)
        .lines()
        .map(|line| line.trim().replace("\\\\", "\\"))
        .filter(|line| !line.is_empty())
        .collect()
}

#[test]
#[ignore = "rewrites real grants on a real MariaDB: polygon only"]
fn the_repair_narrows_a_wildcard_grant_an_older_agent_issued_and_touches_nothing_else() {
    // The premise first, for THIS pair: if the two names cannot collide, the
    // seeded defect below is not a defect and nothing here measures anything.
    the_geometry_that_makes_a_collision_possible(REPAIR_VICTIM_ACCOUNT, REPAIR_ATTACKER_ACCOUNT);

    let server = PolygonMariadb::start();
    let victim = PolygonAccount::create(REPAIR_VICTIM_ACCOUNT);
    let attacker = PolygonAccount::create(REPAIR_ATTACKER_ACCOUNT);
    clear(&server, victim.name());
    clear(&server, attacker.name());
    server.run(&format!("DROP DATABASE IF EXISTS `{FOREIGN_DATABASE}`"));
    server.run(&format!("DROP USER IF EXISTS '{FOREIGN_USER}'@'localhost'"));

    let host = ProcessDbHost::new(polygon_distro());
    create_database(&host, &request_for(victim.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the victim's database must be created: {error}"));
    create_database(&host, &request_for(attacker.name(), CUSTOMER_PASSWORD))
        .unwrap_or_else(|error| panic!("the attacker's database must be created: {error}"));

    let victim_login = format!("{}_shop", victim.name().as_str());
    let attacker_login = format!("{}_shop", attacker.name().as_str());
    let victim_database = victim_login.clone();
    let attacker_database = attacker_login.clone();

    // SEEDING THE PRE-FIX STATE, with the client and not with the code under
    // test: exactly the two statements a host that created this database before
    // the escape existed would be carrying. The revoke of the escaped row first,
    // so the result is a host with ONE grant and that grant a pattern.
    server.run(&format!(
        "REVOKE ALL PRIVILEGES ON `{}`.* FROM '{attacker_login}'@'localhost'",
        attacker_database.replace('_', "\\_")
    ));
    server.run(&format!(
        "GRANT ALL PRIVILEGES ON `{attacker_database}`.* TO '{attacker_login}'@'localhost'"
    ));

    // A grant this panel did NOT issue, at risk in exactly the same way, which
    // must come out of the pass untouched.
    server.run(&format!("CREATE DATABASE `{FOREIGN_DATABASE}`"));
    server.run(&format!(
        "CREATE USER '{FOREIGN_USER}'@'localhost' IDENTIFIED BY '{FOREIGN_PASSWORD}'"
    ));
    server.run(&format!(
        "GRANT ALL PRIVILEGES ON `{FOREIGN_DATABASE}`.* TO '{FOREIGN_USER}'@'localhost'"
    ));
    server.run(&format!("CREATE TABLE `{FOREIGN_DATABASE}`.rows (id INT)"));

    server.run(&format!(
        "CREATE TABLE `{victim_database}`.customers (id INT, secret VARCHAR(64))"
    ));
    server.run(&format!(
        "INSERT INTO `{victim_database}`.customers VALUES (1, '{VICTIM_SECRET}')"
    ));

    // POSITIVE CONTROL on the axis that can go blind: the seeded state must
    // really be exploitable. If this read were refused — a mistyped seed, a
    // geometry that cannot collide, a server that ignored the pattern — then the
    // refusal asserted after the repair would prove nothing at all.
    let before = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT secret FROM `{victim_database}`.customers"),
    );
    assert!(
        before.status.success() && String::from_utf8_lossy(&before.stdout).contains(VICTIM_SECRET),
        "the seeded pre-fix grant must actually reach the victim's data, or this \
         test measures a repair of nothing:\n{}",
        String::from_utf8_lossy(&before.stderr)
    );

    // A report-only pass must change NOTHING. Asserted by the attacker still
    // reaching the victim afterwards, which is the strongest form of "nothing
    // changed" available here.
    let planned = repair_grants(&host, true).unwrap_or_else(|error| panic!("report-only: {error}"));
    assert!(
        planned.repaired.is_empty(),
        "a report-only pass must rewrite nothing"
    );
    assert!(
        planned
            .would_repair
            .iter()
            .any(|grant| grant.database.as_str() == attacker_database),
        "a report-only pass must name the row it would rewrite: {:?}",
        planned.would_repair
    );
    let still_open = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT secret FROM `{victim_database}`.customers"),
    );
    assert!(
        still_open.status.success(),
        "a report-only pass must not have taken the wildcard away"
    );

    // THE REPAIR.
    let report = repair_grants(&host, false).unwrap_or_else(|error| panic!("the repair: {error}"));
    assert!(
        report
            .repaired
            .iter()
            .any(|grant| grant.database.as_str() == attacker_database),
        "the pass must report the row it rewrote: {:?}",
        report.repaired
    );

    // (a) The colliding credential is now DENIED on the victim's data.
    let refused = server.run_as(
        &attacker_login,
        CUSTOMER_PASSWORD,
        &format!("SELECT secret FROM `{victim_database}`.customers"),
    );
    assert!(
        !refused.status.success(),
        "after the repair the attacker's credential must no longer reach the \
         victim's database"
    );
    let complaint = String::from_utf8_lossy(&refused.stderr);
    assert!(
        complaint.contains("denied"),
        "the refusal must be the server DENYING access rather than any other \
         failure:\n{complaint}"
    );
    assert!(
        !String::from_utf8_lossy(&refused.stdout).contains(VICTIM_SECRET),
        "the victim's row must not appear in the attacker's output"
    );

    // (b) INVERSE CONTROL, mandatory: both owners still reach their OWN
    //     databases. A repair that revoked and failed to re-grant would pass
    //     every assertion above while taking every customer's application
    //     offline — the worse defect.
    for (login, database) in [
        (&attacker_login, &attacker_database),
        (&victim_login, &victim_database),
    ] {
        let own = server.run_as(
            login,
            CUSTOMER_PASSWORD,
            &format!("CREATE TABLE `{database}`.after_repair (id INT)"),
        );
        assert!(
            own.status.success(),
            "{login} must still hold full privileges on its own database after \
             the repair:\n{}",
            String::from_utf8_lossy(&own.stderr)
        );
    }

    // (c) A second run changes nothing: the stored patterns are identical and
    //     the pass reports no repair at all.
    let after_first = stored_patterns(&server, &attacker_login);
    let again =
        repair_grants(&host, false).unwrap_or_else(|error| panic!("the second run: {error}"));
    assert!(
        again.repaired.is_empty(),
        "a second run must rewrite nothing: {:?}",
        again.repaired
    );
    assert!(
        !again.refused.iter().any(|row| row.user == attacker_login),
        "a second run must RECOGNISE the row it wrote itself rather than \
         reporting it as one it cannot classify — an operator who is told a \
         repaired row is unclassifiable has been handed the defect back: {:?}",
        again.refused
    );
    assert_eq!(
        stored_patterns(&server, &attacker_login),
        after_first,
        "a second run must leave the grant table byte-identical"
    );
    assert_eq!(
        after_first,
        vec![attacker_database.replace('_', "\\_")],
        "the only row left for this login must be the escaped pattern"
    );

    // (d) The foreign grant is untouched, and that is OBSERVED rather than
    //     assumed: its stored pattern is still the unescaped one, it is reported
    //     as refused with a reason, and its user still reaches its database.
    assert_eq!(
        stored_patterns(&server, FOREIGN_USER),
        vec![FOREIGN_DATABASE.to_owned()],
        "a grant this panel did not issue must still be stored exactly as the \
         operator wrote it"
    );
    assert!(
        report
            .refused
            .iter()
            .any(|row| row.database == FOREIGN_DATABASE
                && row.user == FOREIGN_USER
                && row.reason == GrantRepairRefusal::NotThePanelsNaming),
        "the foreign row must be reported as refused, with its reason, so that \
         'left alone' is something an operator reads: {:?}",
        report.refused
    );
    let foreign_still_works = server.run_as(
        FOREIGN_USER,
        FOREIGN_PASSWORD,
        &format!("INSERT INTO `{FOREIGN_DATABASE}`.rows VALUES (1)"),
    );
    assert!(
        foreign_still_works.status.success(),
        "the operator's own grant must still work:\n{}",
        String::from_utf8_lossy(&foreign_still_works.stderr)
    );

    server.run(&format!("DROP DATABASE IF EXISTS `{FOREIGN_DATABASE}`"));
    server.run(&format!("DROP USER IF EXISTS '{FOREIGN_USER}'@'localhost'"));
    clear(&server, victim.name());
    clear(&server, attacker.name());
}
