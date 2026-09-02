//! What the diagnostic listing decodes, and — the point of the file — what it
//! refuses to decode.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, account};
use crate::db::list_databases::list_databases;

/// A sub-account's database is not aliased onto the account whose name is a
/// prefix of it.
#[test]
fn the_diagnostic_list_decodes_by_last_underscore_and_does_not_alias_a_sub_account() {
    // `alice_bob_shop` belongs to account `alice_bob`, not `alice`. A
    // starts_with("alice_") filter would leak it. The decode-by-last-underscore
    // + whole-account match must NOT return it for `alice`.
    let host = FakeDbHost::with_existing_many(&[
        "alice_shop",
        "alice_blog",
        "alice_bob_shop",
        "bob_shop",
        "mysql",
    ]);

    let listed = list_databases(&host, &account()).expect("listed");

    let names: Vec<&str> = listed.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(names, vec!["alice_blog", "alice_shop"]); // alice_bob_shop is NOT alice's
}

/// The sub-account sees its own database, which is the other half of the same
/// property: the decode is exact, not merely strict.
#[test]
fn the_sub_account_is_the_one_that_sees_its_own_database() {
    let host = FakeDbHost::with_existing_many(&["alice_shop", "alice_bob_shop"]);
    let sub_account = AccountName::parse("alice_bob").expect("valid");

    let listed = list_databases(&host, &sub_account).expect("listed");

    let names: Vec<&str> = listed.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(names, vec!["alice_bob_shop"]);
}

/// The server's own databases carry no separator and so belong to nobody.
#[test]
fn the_servers_own_databases_are_listed_for_no_account() {
    let host =
        FakeDbHost::with_existing_many(&["mysql", "information_schema", "performance_schema"]);

    let listed = list_databases(&host, &account()).expect("listed");

    // `performance_schema` does contain an underscore, so it decodes to an
    // account named `performance` — which is not `alice`, and would be a real
    // leak only if some account were named `performance`. It is refused here
    // for the same reason `alice_bob_shop` is: the whole account must match.
    assert!(listed.is_empty());
}

/// An account with nothing on the server lists nothing.
#[test]
fn an_account_that_owns_nothing_lists_nothing() {
    let host = FakeDbHost::new();

    assert!(
        list_databases(&host, &account())
            .expect("listed")
            .is_empty()
    );
}

/// The order is the listing's own, not whatever order the server printed.
#[test]
fn the_listing_is_sorted_regardless_of_the_order_the_server_printed() {
    let host = FakeDbHost::with_existing_many(&["alice_shop", "alice_apples", "alice_blog"]);

    let listed = list_databases(&host, &account()).expect("listed");

    let names: Vec<&str> = listed.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(names, vec!["alice_apples", "alice_blog", "alice_shop"]);
}

/// A name outside the convention decodes to nothing rather than being reported
/// under a name this agent could not have created.
#[test]
fn a_name_the_agent_could_not_have_created_is_not_listed() {
    // Made by hand by an administrator: the suffix carries a capital letter,
    // which `for_account` refuses, so there is no `DatabaseName` for it.
    let host = FakeDbHost::with_existing_many(&["alice_Legacy", "alice_shop"]);

    let listed = list_databases(&host, &account()).expect("listed");

    let names: Vec<&str> = listed.iter().map(|d| d.name.as_str()).collect();
    assert_eq!(names, vec!["alice_shop"]);
}

/// A server that refuses the connection is reported as such, not as an empty
/// listing.
#[test]
fn a_server_that_refuses_the_connection_is_not_reported_as_an_empty_listing() {
    let host = FakeDbHost::failing_with(1045, "Access denied for user 'root'@'localhost'");

    let failure = list_databases(&host, &account()).expect_err("must fail");

    assert!(matches!(failure, DbError::AccessDenied));
}
