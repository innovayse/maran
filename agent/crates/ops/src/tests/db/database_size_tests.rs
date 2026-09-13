//! What `database_size` reports, and what it refuses to guess at.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::db::database_size::database_size;
use crate::db::db_error::DbError;
use crate::db::fake_db_host::{FakeDbHost, shop_database};

/// The server's figure is what is reported.
#[test]
fn the_size_is_the_number_the_server_printed() {
    let host = FakeDbHost::with_existing("alice_shop");
    host.set_size_output("1048576\n");

    let report = database_size(&host, &shop_database()).expect("measured");

    assert_eq!(report.bytes, 1_048_576);
}

/// An empty database is reported as zero rather than as unreadable.
#[test]
fn a_database_with_no_tables_is_reported_as_zero_bytes() {
    let host = FakeDbHost::with_existing("alice_shop");
    host.set_size_output("0\n");

    assert_eq!(
        database_size(&host, &shop_database())
            .expect("measured")
            .bytes,
        0
    );
}

/// A database that is not there is `NotFound`, never a zero-byte answer.
#[test]
fn measuring_a_database_that_is_not_there_reports_not_found_and_not_zero() {
    let host = FakeDbHost::new();

    let failure = database_size(&host, &shop_database()).expect_err("must fail");

    // The sum answers `0` for a missing database exactly as it does for an
    // empty one, so without the existence check a caller would be told a
    // database it never created is empty.
    assert!(matches!(failure, DbError::NotFound));
}

/// An answer that is not a number is refused rather than guessed at.
#[test]
fn an_answer_that_is_not_a_number_is_refused_as_unparsable() {
    let host = FakeDbHost::with_existing("alice_shop");
    host.set_size_output("NULL\n");

    let failure = database_size(&host, &shop_database()).expect_err("must fail");

    assert!(matches!(failure, DbError::Unparsable));
}

/// The measured name reaches the statement prefixed, and only prefixed.
#[test]
fn the_measurement_names_the_prefixed_database() {
    let host = FakeDbHost::with_existing("alice_shop");

    database_size(&host, &shop_database()).expect("measured");

    let query = host
        .statements()
        .into_iter()
        .find(|statement| statement.starts_with("SELECT COALESCE("))
        .expect("a size query was sent");
    assert!(query.contains("table_schema = 'alice_shop'"));
}
