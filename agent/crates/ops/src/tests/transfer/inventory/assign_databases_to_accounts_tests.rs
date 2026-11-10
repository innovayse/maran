//! Tests for [`assign_databases_to_accounts`]: attributing databases to accounts.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::transfer::inventory::assign_databases_to_accounts::assign_databases_to_accounts;
use crate::transfer::inventory::inventory_fixtures::passwd_entry;
use crate::transfer::inventory::model::listed_database::ListedDatabase;

/// A listed database.
fn listed(name: &str, size: Option<u64>) -> ListedDatabase {
    ListedDatabase {
        name: name.to_owned(),
        size_bytes: size,
    }
}

/// A database named after its account's prefix goes to that account.
#[test]
fn a_prefixed_database_goes_to_its_account() {
    let acme = passwd_entry("acme", "/home/acme");
    let shop = passwd_entry("shopsite", "/home/shopsite");

    let assignment = assign_databases_to_accounts(
        vec![listed("acme_wp", Some(10)), listed("shopsite_blog", None)],
        &[&acme, &shop],
    );

    assert_eq!(assignment.per_account[0][0].name.as_str(), "acme_wp");
    assert_eq!(assignment.per_account[0][0].size_bytes, Some(10));
    assert_eq!(assignment.per_account[1][0].name.as_str(), "shopsite_blog");
    assert_eq!(assignment.refused, 0);
}

/// A database with no prefix, with an uppercase letter, or with a prefix no
/// account has is refused and never renamed.
#[test]
fn databases_that_decode_for_no_account_are_refused() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_databases_to_accounts(
        vec![
            listed("orphan", None),
            listed("acme_Bad", None),
            listed("other_db", None),
            listed("acme_with_more", None),
        ],
        &[&acme],
    );

    assert!(assignment.per_account[0].is_empty());
    assert_eq!(assignment.refused, 4);
}

/// The owner is the text before the LAST underscore: an account whose own name
/// holds an underscore owns its databases, and its prefix is not another
/// account's.
#[test]
fn the_owner_is_the_text_before_the_last_underscore() {
    let short = passwd_entry("acme", "/home/acme");
    let long = passwd_entry("acme_two", "/home/acme_two");

    let assignment =
        assign_databases_to_accounts(vec![listed("acme_two_wp", None)], &[&short, &long]);

    assert!(assignment.per_account[0].is_empty());
    assert_eq!(assignment.per_account[1].len(), 1);
}

/// Several databases of one account are all kept, in order.
#[test]
fn several_databases_of_one_account_are_kept_in_order() {
    let acme = passwd_entry("acme", "/home/acme");

    let assignment = assign_databases_to_accounts(
        vec![listed("acme_b", None), listed("acme_a", None)],
        &[&acme],
    );

    let names: Vec<&str> = assignment.per_account[0]
        .iter()
        .map(|d| d.name.as_str())
        .collect();
    assert_eq!(names, ["acme_b", "acme_a"]);
}
