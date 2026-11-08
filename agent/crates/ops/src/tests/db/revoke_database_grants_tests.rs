//! Which grants `revoke_database_grants` takes away, and which it refuses to touch.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::db::create_database::create_database;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, shop_database, shop_request};
use crate::db::revoke_database_grants::revoke_database_grants;

/// The grant a stranger holds on `alice`'s `shop`, escaped as `create_database` writes it.
fn stranger_grant(host: &FakeDbHost, database: &str, user: &str) {
    host.add_grant_row(
        "localhost",
        database,
        user,
        &format!("GRANT ALL PRIVILEGES ON `{database}`.* TO `{user}`@`localhost`"),
    );
}

/// A grant held by a user other than the dedicated one is revoked too.
#[test]
fn a_grant_held_by_a_user_other_than_the_dedicated_one_is_revoked() {
    let host = FakeDbHost::new();
    create_database(&host, &shop_request()).unwrap();
    stranger_grant(&host, "alice\\_shop", "someone_else");

    revoke_database_grants(&host, &shop_database()).unwrap();

    assert!(host.granted_databases().is_empty());
}

/// The unescaped spelling an older agent wrote is revoked as well.
#[test]
fn a_grant_in_the_unescaped_spelling_is_revoked() {
    let host = FakeDbHost::new();
    stranger_grant(&host, "alice_shop", "old_user");

    revoke_database_grants(&host, &shop_database()).unwrap();

    assert!(host.granted_databases().is_empty());
}

/// A grant on another database, or a wildcard that is not this database's, stays.
#[test]
fn grants_on_other_databases_and_wildcards_are_left_alone() {
    let host = FakeDbHost::new();
    stranger_grant(&host, "alice\\_blog", "blogger");
    stranger_grant(&host, "alice%", "wide");

    revoke_database_grants(&host, &shop_database()).unwrap();

    assert_eq!(host.granted_databases().len(), 2);
    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| statement.starts_with("REVOKE "))
    );
}

/// The statement revokes grant option too, so no row survives with only that.
#[test]
fn the_revoke_also_removes_grant_option() {
    let host = FakeDbHost::new();
    stranger_grant(&host, "alice\\_shop", "someone");

    revoke_database_grants(&host, &shop_database()).unwrap();

    assert_eq!(
        host.statements().last().unwrap(),
        "REVOKE ALL PRIVILEGES, GRANT OPTION ON `alice\\_shop`.* FROM 'someone'@'localhost'"
    );
}

/// Nothing to revoke sends nothing but the read, and a repeat is the same.
#[test]
fn a_second_pass_sends_no_revoke() {
    let host = FakeDbHost::new();
    stranger_grant(&host, "alice\\_shop", "someone");

    revoke_database_grants(&host, &shop_database()).unwrap();
    let after_first = host.statements().len();
    revoke_database_grants(&host, &shop_database()).unwrap();

    assert_eq!(host.statements().len(), after_first + 1, "only the read");
}

/// A user outside the allow-list stops the operation before any revoke.
#[test]
fn a_user_with_a_quote_in_its_name_is_refused_and_nothing_is_revoked() {
    let host = FakeDbHost::new();
    stranger_grant(&host, "alice\\_shop", "fine");
    stranger_grant(&host, "alice\\_shop", "x' OR '1");

    let outcome = revoke_database_grants(&host, &shop_database());

    assert!(matches!(outcome, Err(DbError::Unparsable)), "{outcome:?}");
    assert!(
        !host
            .statements()
            .iter()
            .any(|statement| statement.starts_with("REVOKE "))
    );
}

/// A host outside the allow-list is refused the same way.
#[test]
fn a_host_with_a_backtick_is_refused() {
    let host = FakeDbHost::new();
    host.add_grant_row("lo`cal", "alice\\_shop", "someone", "GRANT");

    let outcome = revoke_database_grants(&host, &shop_database());

    assert!(matches!(outcome, Err(DbError::Unparsable)), "{outcome:?}");
}

/// A grant at another host is revoked at that host.
#[test]
fn a_grant_from_another_host_is_revoked_at_that_host() {
    let host = FakeDbHost::new();
    host.add_grant_row("10.0.0.%", "alice\\_shop", "remote", "GRANT");

    revoke_database_grants(&host, &shop_database()).unwrap();

    assert_eq!(
        host.statements().last().unwrap(),
        "REVOKE ALL PRIVILEGES, GRANT OPTION ON `alice\\_shop`.* FROM 'remote'@'10.0.0.%'"
    );
}
