//! How many operations are admitted, and that every refusal is clean.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::validation::system::name::AccountName;

use super::BoundedSlots;
use crate::utils::slots_full::SlotsFull;

fn account(name: &str) -> AccountName {
    AccountName::parse(name).unwrap()
}

#[test]
fn an_account_is_admitted_up_to_its_ceiling_and_refused_past_it() {
    let slots = BoundedSlots::new(10, 2);
    let alice = account("alice");

    let first = slots.try_acquire(&alice).unwrap();
    let second = slots.try_acquire(&alice).unwrap();
    let third = slots.try_acquire(&alice);

    assert_eq!(third.err(), Some(SlotsFull));
    drop((first, second));
}

#[test]
fn one_accounts_ceiling_does_not_refuse_another_account() {
    let slots = BoundedSlots::new(10, 1);
    let _alice = slots.try_acquire(&account("alice")).unwrap();

    assert!(slots.try_acquire(&account("bob")).is_ok());
}

#[test]
fn the_host_ceiling_refuses_an_account_that_is_under_its_own() {
    let slots = BoundedSlots::new(2, 5);
    let _alice = slots.try_acquire(&account("alice")).unwrap();
    let _bob = slots.try_acquire(&account("bob")).unwrap();

    assert_eq!(slots.try_acquire(&account("carol")).err(), Some(SlotsFull));
}

#[test]
fn a_finished_operation_gives_both_counts_back() {
    let slots = BoundedSlots::new(1, 1);
    let alice = account("alice");

    drop(slots.try_acquire(&alice).unwrap());

    assert!(slots.try_acquire(&alice).is_ok());
}

#[test]
fn an_account_refused_at_its_own_ceiling_takes_nothing_from_the_host() {
    let slots = BoundedSlots::new(2, 1);
    let alice = account("alice");
    let _held = slots.try_acquire(&alice).unwrap();

    assert!(slots.try_acquire(&alice).is_err());

    // The host still has its second slot for somebody else.
    assert!(slots.try_acquire(&account("bob")).is_ok());
}

#[test]
fn a_refusal_by_the_host_gives_back_the_account_permit_it_had_taken() {
    // An account permit taken and then not returned when the host count refuses
    // would shrink the account's ceiling by one on every refused attempt.
    let slots = BoundedSlots::new(1, 1);
    let bob = slots.try_acquire(&account("bob")).unwrap();
    let alice = account("alice");

    assert!(slots.try_acquire(&alice).is_err());
    drop(bob);

    assert!(slots.try_acquire(&alice).is_ok());
}
