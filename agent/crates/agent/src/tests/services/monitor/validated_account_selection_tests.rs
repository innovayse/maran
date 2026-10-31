//! Tests for the account names a disk-usage request carries.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_ops::monitor::AccountSelection;

use super::validated_account_selection;
use crate::proto::ErrorCode;

#[test]
fn an_empty_list_selects_every_hosting_account() {
    // What an older panel that never sets the field sends, and its meaning is kept.
    assert_eq!(
        validated_account_selection(&[]).expect("an empty list is a complete request"),
        AccountSelection::Every
    );
}

#[test]
fn a_list_selects_exactly_the_accounts_it_names() {
    let selection =
        validated_account_selection(&["alice".to_owned(), "bob".to_owned()]).expect("valid names");

    let alice = AccountName::parse("alice").unwrap();
    let bob = AccountName::parse("bob").unwrap();
    let carol = AccountName::parse("carol").unwrap();
    assert!(selection.includes(&alice));
    assert!(selection.includes(&bob));
    assert!(!selection.includes(&carol));
}

#[test]
fn one_invalid_name_refuses_the_whole_request() {
    // Dropping it instead would make an account vanish from the answer exactly
    // as a deleted one does.
    let refusal = validated_account_selection(&["alice".to_owned(), "../root".to_owned()])
        .expect_err("a traversal is not an account name");

    assert_eq!(refusal.code, ErrorCode::InvalidInput as i32);
}

#[test]
fn an_empty_name_inside_a_list_is_refused_rather_than_read_as_every_account() {
    let refusal =
        validated_account_selection(&[String::new()]).expect_err("an empty name is not a name");

    assert_eq!(refusal.code, ErrorCode::InvalidInput as i32);
}
