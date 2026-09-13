//! What `set_database_password` changes, what it refuses, and what it never says.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::db::database_name::DatabaseName;
use maran_agent_core::validation::db::db_user_name::DbUserName;
use maran_agent_core::validation::secrets::password::Password;

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, account, shop_request, shop_user};
use crate::db::model::create_database_request::CreateDatabaseRequest;
use crate::db::set_database_password::set_database_password;

/// The value every test here resets to.
fn replacement() -> Password {
    Password::parse("Replaced-2026").expect("valid")
}

/// A host that already holds `alice`'s `shop` database and its user.
fn host_with_shop() -> FakeDbHost {
    let host = FakeDbHost::new();
    create_database(&host, &shop_request()).expect("created");

    host
}

/// The server ends up holding the new value.
#[test]
fn setting_a_password_replaces_the_one_the_user_was_created_with() {
    let host = host_with_shop();

    set_database_password(&host, &shop_user(), &replacement()).expect("set");

    assert_eq!(
        host.password_of("alice_shop"),
        Some("Replaced-2026".to_owned())
    );
}

/// A user that is not on this server is `NotFound`, and nothing is created.
#[test]
fn setting_a_password_for_a_user_that_is_not_there_reports_not_found() {
    let host = FakeDbHost::new();

    let failure =
        set_database_password(&host, &shop_user(), &replacement()).expect_err("must fail");

    assert!(matches!(failure, DbError::NotFound));
    assert!(
        host.users().is_empty(),
        "a reset must never mint a login the panel has no row for"
    );
}

/// The refusal happens before anything is altered.
#[test]
fn a_user_that_is_not_there_is_refused_before_any_alter_is_sent() {
    let host = FakeDbHost::new();

    let _ = set_database_password(&host, &shop_user(), &replacement());

    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| statement.starts_with("ALTER USER ")),
        "an alter must not be attempted for a user that is not there"
    );
}

/// Only the prefixed name reaches the server, and it is the localhost login.
#[test]
fn the_alter_names_the_prefixed_user_at_localhost_and_never_a_bare_one() {
    let host = host_with_shop();

    set_database_password(&host, &shop_user(), &replacement()).expect("set");

    let alter = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("ALTER USER "))
        .expect("an alter was sent");
    assert_eq!(
        alter,
        "ALTER USER 'alice_shop'@'localhost' IDENTIFIED BY 'Replaced-2026'"
    );
}

/// The existence question names the same host the grant was made for.
#[test]
fn the_existence_check_asks_about_the_localhost_login_and_not_the_bare_name() {
    let host = host_with_shop();

    set_database_password(&host, &shop_user(), &replacement()).expect("set");

    // Two logins can share a name and differ by host. Asking without the host
    // would answer "yes" for a user this statement then fails to alter, and the
    // operation would report a change it did not make.
    let question = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("SELECT COUNT(*) FROM mysql.user "))
        .expect("existence was asked");
    assert_eq!(
        question,
        "SELECT COUNT(*) FROM mysql.user WHERE user = 'alice_shop' AND host = 'localhost'"
    );
}

/// Setting a password twice leaves the second one, which is all convergence can
/// mean for a credential.
#[test]
fn setting_a_password_twice_leaves_the_second_value() {
    let host = host_with_shop();

    set_database_password(&host, &shop_user(), &replacement()).expect("set");
    set_database_password(
        &host,
        &shop_user(),
        &Password::parse("Third-value").expect("valid"),
    )
    .expect("set again");

    assert_eq!(
        host.password_of("alice_shop"),
        Some("Third-value".to_owned())
    );
}

/// A reset addresses only the user it was given.
#[test]
fn setting_one_users_password_leaves_another_users_alone() {
    let host = host_with_shop();
    let other = DbUserName::for_account(&account(), "blog").expect("valid");
    create_database(
        &host,
        &CreateDatabaseRequest {
            database: DatabaseName::for_account(&account(), "blog").expect("valid"),
            user: other.clone(),
            password: Password::parse("Blog-pw2026").expect("valid"),
        },
    )
    .expect("created");

    set_database_password(&host, &shop_user(), &replacement()).expect("set");

    assert_eq!(
        host.password_of(other.as_str()),
        Some("Blog-pw2026".to_owned())
    );
}

/// A server refusing the agent's own connection is not reported as "not found".
#[test]
fn a_server_that_refuses_the_agents_connection_is_not_reported_as_not_found() {
    // The two answers send an operator to opposite places: NotFound says the
    // customer's user is gone, AccessDenied says socket authentication is not
    // enabled for root@localhost. Reading one as the other is how an operator
    // recreates a database that was never missing.
    let host = FakeDbHost::failing_with(1045, "Access denied for user 'root'@'localhost'");

    let failure =
        set_database_password(&host, &shop_user(), &replacement()).expect_err("must fail");

    assert!(matches!(failure, DbError::AccessDenied));
}

/// An existence count that is not a number is refused rather than guessed at.
#[test]
fn an_existence_answer_that_is_not_a_number_is_unparsable_rather_than_absent() {
    let host = host_with_shop();
    host.set_user_count_output("something the parser cannot read");

    let failure =
        set_database_password(&host, &shop_user(), &replacement()).expect_err("must fail");

    assert!(matches!(failure, DbError::Unparsable));
}
