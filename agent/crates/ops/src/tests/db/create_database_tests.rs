//! What `create_database` sends, what it refuses to send, and what it makes of
//! a refusal.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::secrets::password::Password;

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, shop_request};

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

    let grant = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("GRANT "))
        .expect("a grant was sent");
    assert!(grant.contains("`alice_shop`.*"));
    // `ON *.*` would give one customer's application every other customer's
    // data on the host.
    assert!(!grant.contains("*.*'"));
    assert!(!grant.contains(" ON *.*"));
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
