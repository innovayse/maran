//! What `create_database` sends, what it refuses to send, and what it makes of
//! a refusal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;
use maran_agent_core::validation::system::name::AccountName;

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, shop_request};
use crate::db::model::create_database_request::CreateDatabaseRequest;

/// The prefixed name is what reaches the server, and the bare requested name
/// never does.
#[test]
fn creating_a_database_asks_the_client_for_the_prefixed_name_and_never_the_requested_one() {
    let host = FakeDbHost::new();
    let request = shop_request();

    create_database(&host, &request).expect("created");

    let statements = host.statements();
    assert!(statements.iter().any(|s| s.contains("alice_shop")));
    assert!(
        !statements.iter().any(|s| s.contains("`shop`")),
        "the bare requested name must never reach MySQL"
    );
}

/// A password carrying a quote has no value of the type, so there is nothing to
/// hand the operation.
#[test]
fn a_password_the_type_forbids_cannot_be_constructed_so_it_cannot_reach_the_statement() {
    // The injection this closes is a quote in the password breaking out of
    // IDENTIFIED BY '…'. The defence is that such a password has no Password
    // value, so there is nothing to pass to create_database. This is the
    // "validated, not escaped" guarantee, at the type boundary.
    assert!(Password::parse("pw' OR '1'='1").is_err());
}

/// A refusal arrives as a named variant, and the client's own words do not
/// arrive at all.
#[test]
fn an_error_from_the_client_carries_a_typed_variant_and_not_the_raw_stderr() {
    // The realistic leak is the client quoting the credential back. The error
    // must be a typed DbError, never the client's stdout/stderr verbatim.
    let host = FakeDbHost::failing_with(
        1045,
        "Access denied for user 'alice_shop'@'localhost' (using password: Gen3rated-pw)",
    );

    let error = create_database(&host, &shop_request()).expect_err("must fail");

    assert!(matches!(error, DbError::AccessDenied));
    let printed = format!("{error:?} {error}");
    assert!(!printed.contains("Gen3rated-pw"));
    assert!(!printed.contains("Access denied for user"));
}

/// A repeat converges on `AlreadyExists` instead of failing.
#[test]
fn creating_a_database_that_already_exists_reports_already_exists_rather_than_failing() {
    // Idempotency, per the standing rule: repeating an operation converges.
    let host = FakeDbHost::with_existing("alice_shop");

    assert!(matches!(
        create_database(&host, &shop_request()),
        Err(DbError::AlreadyExists)
    ));
}

/// Creating the same database twice against one server converges the second
/// time, without a pre-seeded fake.
#[test]
fn creating_the_same_database_twice_converges_on_already_exists() {
    let host = FakeDbHost::new();
    let request = shop_request();

    create_database(&host, &request).expect("created");
    let second = create_database(&host, &request);

    assert!(matches!(second, Err(DbError::AlreadyExists)));
    assert_eq!(host.databases(), vec!["alice_shop".to_owned()]);
}

/// The grant covers the one database and not the whole server.
#[test]
fn the_user_is_granted_privileges_on_its_own_database_only() {
    let host = FakeDbHost::new();

    create_database(&host, &shop_request()).expect("created");

    let grant = granted_pattern(&host);
    // The separator is escaped, because this position of the statement is a
    // pattern rather than an identifier. The assertion that used to stand here
    // read `contains("`alice_shop`.*")`, which the unescaped form satisfies —
    // it was on the `*.*` axis only, and could not see the wildcard inside the
    // name at all.
    assert_eq!(grant, "alice\\_shop");
    // `ON *.*` would give one customer's application every other customer's
    // data on the host.
    let statement = grant_statement(&host);
    assert!(!statement.contains("*.*'"));
    assert!(!statement.contains(" ON *.*"));
}

/// An underscore in the account's own name does not let its grant reach another
/// account's database.
///
/// **The axis this is on.** A database-level `GRANT` takes its database name as a
/// LIKE-style pattern, so `_` there matches any single character and the name is
/// only scoped once it is escaped. Account `h_stco` asking for `main` produces
/// `h_stco_main`, whose unescaped form is the pattern `h?stco?main` — and that
/// matches account `hostco`'s `hostco_main` exactly.
///
/// **UNOBSERVED HERE: this is not a database server.** [`matches_as_like`] is
/// this test's model of MySQL's pattern semantics, not MySQL's answer, so what
/// is proved is that the statement carries no unescaped wildcard — not that the
/// server reads it as this test does. The inverse control below is what stops
/// the model being vacuous: fed the UNESCAPED name, the same matcher does reach
/// the victim, so the matcher can tell the two apart and the difference this
/// test asserts is a difference it can see.
#[test]
fn a_grant_for_an_account_whose_name_holds_the_separator_cannot_reach_a_neighbours_database() {
    let attacker = AccountName::parse("h_stco").expect("valid");
    let host = FakeDbHost::new();

    create_database(
        &host,
        &CreateDatabaseRequest {
            database: DatabaseName::for_account(&attacker, "main").expect("valid"),
            user: DbUserName::for_account(&attacker, "main").expect("valid"),
            password: Password::parse("Gen3rated-pw").expect("valid"),
        },
    )
    .expect("created");

    let granted = granted_pattern(&host);
    let victim = "hostco_main";

    // Positive control: the grant still covers the database it was issued for.
    // Without this the property below is satisfied by a grant that reaches
    // nothing at all, which would break every customer's own application.
    assert!(
        matches_as_like(&granted, "h_stco_main"),
        "the grant must still cover its own database; got `{granted}`"
    );
    assert!(
        !matches_as_like(&granted, victim),
        "`{granted}` reaches another account's `{victim}`"
    );
    // Inverse control on the MATCHER: the unescaped name, which is what this
    // code used to send, does reach the victim. A matcher that answered false
    // for everything would pass the assertion above while measuring nothing.
    assert!(
        matches_as_like("h_stco_main", victim),
        "the matcher cannot see the difference this test is about"
    );
}

/// The database-level pattern this agent granted on, taken out of the statement.
fn granted_pattern(host: &FakeDbHost) -> String {
    let statement = grant_statement(host);
    let after = statement
        .split_once("ON `")
        .expect("a grant names a database")
        .1;

    after
        .split_once("`.*")
        .expect("a grant scopes to one database")
        .0
        .to_owned()
}

/// The one `GRANT` the operation sent.
fn grant_statement(host: &FakeDbHost) -> String {
    host.statements()
        .into_iter()
        .find(|statement| statement.starts_with("GRANT "))
        .expect("a grant was sent")
}

/// Whether `pattern` matches `name` the way a database server's `LIKE` does.
///
/// `_` matches exactly one character, `%` any run of them, and a backslash makes
/// the next character mean itself. This is the semantics the server applies to a
/// database-level grant's database name, written out here because there is no
/// server in a unit test — see the blind spot stated on the test above.
fn matches_as_like(pattern: &str, name: &str) -> bool {
    let pattern: Vec<char> = pattern.chars().collect();
    let name: Vec<char> = name.chars().collect();

    like_from(&pattern, &name)
}

/// Whether the whole of `pattern` matches the whole of `name`.
fn like_from(pattern: &[char], name: &[char]) -> bool {
    match pattern.split_first() {
        None => name.is_empty(),
        Some(('%', rest)) => (0..=name.len()).any(|taken| like_from(rest, &name[taken..])),
        Some(('_', rest)) => !name.is_empty() && like_from(rest, &name[1..]),
        Some(('\\', rest)) => match rest.split_first() {
            // A trailing backslash stands for itself.
            None => name == ['\\'],
            Some((escaped, tail)) => name.first() == Some(escaped) && like_from(tail, &name[1..]),
        },
        Some((literal, rest)) => name.first() == Some(literal) && like_from(rest, &name[1..]),
    }
}

/// The user is created, so a customer has something to connect with.
#[test]
fn the_dedicated_user_is_created_alongside_the_database() {
    let host = FakeDbHost::new();

    create_database(&host, &shop_request()).expect("created");

    assert_eq!(host.users(), vec!["alice_shop".to_owned()]);
}

/// Losing the race to another writer between the check and the create is still
/// answered as `AlreadyExists`.
#[test]
fn a_server_that_refuses_because_the_database_exists_still_reports_already_exists() {
    // The pre-check and this classification are two independent guards on the
    // same promise: the check answers the ordinary repeat, and this answers the
    // repeat that arrives while another writer is halfway through the first.
    let host =
        FakeDbHost::failing_with(1007, "Can't create database 'alice_shop'; database exists");

    let failure = create_database(&host, &shop_request()).expect_err("must fail");

    assert!(matches!(failure, DbError::AlreadyExists));
}

/// The dedicated user may connect from this host and from nowhere else.
#[test]
fn the_dedicated_user_may_connect_only_from_this_host() {
    // A database user reachable from anywhere is a database user that can be
    // brute-forced from anywhere, and nothing this panel hosts reaches the
    // server over the network. `'user'@'%'` would undo that in one character.
    let host = FakeDbHost::new();

    create_database(&host, &shop_request()).expect("created");

    for statement in host.statements() {
        assert!(
            !statement.contains("@'%'"),
            "no statement may allow a connection from anywhere: {statement}"
        );
    }

    let created = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("CREATE USER "))
        .expect("a user was created");
    assert!(
        created.contains("'alice_shop'@'localhost'"),
        "the user must be bound to this host: {created}"
    );
}
