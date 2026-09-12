//! One operation per account, and no waiting for the second.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::*;

/// An account name for these tests.
fn account(name: &str) -> AccountName {
    AccountName::parse(name).expect("the fixture name is valid")
}

/// The first taker gets the lock.
#[test]
fn the_first_operation_takes_the_lock() {
    let guard = take_account_lock(&account("lockone"));

    assert!(guard.is_some());
}

/// The second taker is refused immediately rather than queued.
#[test]
fn a_second_operation_on_the_same_account_is_refused_without_waiting() {
    let held = take_account_lock(&account("locktwo"));
    assert!(held.is_some());

    assert!(take_account_lock(&account("locktwo")).is_none());
}

/// Two accounts do not serialise against each other.
#[test]
fn two_accounts_hold_two_different_locks() {
    let first = take_account_lock(&account("lockthree"));
    let second = take_account_lock(&account("lockfour"));

    assert!(first.is_some());
    assert!(second.is_some());
}

/// The lock is released when the guard goes out of scope, by every path.
#[test]
fn releasing_the_guard_lets_the_next_operation_in() {
    drop(take_account_lock(&account("lockfive")));

    assert!(take_account_lock(&account("lockfive")).is_some());
}
