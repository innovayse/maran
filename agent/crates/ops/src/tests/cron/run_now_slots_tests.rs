//! How many runs by hand are admitted, and that every refusal is clean.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;

use super::{ACCOUNT_RUN_NOW_SLOTS, HOST_RUN_NOW_SLOTS, RunNowSlots};
use crate::cron::cron_error::CronError;

fn account(name: &str) -> AccountName {
    AccountName::parse(name).expect("the fixture's own names are valid")
}

#[test]
fn an_account_is_admitted_up_to_its_own_ceiling_and_refused_past_it() {
    let slots = RunNowSlots::new(10, 2);
    let alice = account("alice");

    let first = slots.try_acquire(&alice).expect("first");
    let second = slots.try_acquire(&alice).expect("second");
    let third = slots.try_acquire(&alice);

    assert_eq!(third.err(), Some(CronError::RunCapacityReached));
    drop((first, second));
}

#[test]
fn one_accounts_ceiling_does_not_refuse_another_account() {
    // The inverse control: a count shared by every account would refuse bob here.
    let slots = RunNowSlots::new(10, 1);
    let _alice = slots.try_acquire(&account("alice")).expect("alice");

    let bob = slots.try_acquire(&account("bob"));

    assert!(bob.is_ok());
}

#[test]
fn the_host_ceiling_refuses_an_account_that_is_under_its_own() {
    let slots = RunNowSlots::new(2, 5);
    let _alice = slots.try_acquire(&account("alice")).expect("alice");
    let _bob = slots.try_acquire(&account("bob")).expect("bob");

    let carol = slots.try_acquire(&account("carol"));

    assert_eq!(carol.err(), Some(CronError::RunCapacityReached));
}

#[test]
fn a_finished_run_gives_both_counts_back() {
    let slots = RunNowSlots::new(1, 1);
    let alice = account("alice");
    let held = slots.try_acquire(&alice).expect("held");
    assert!(slots.try_acquire(&alice).is_err());

    drop(held);

    assert!(slots.try_acquire(&alice).is_ok());
}

#[test]
fn a_refusal_by_the_host_gives_back_the_account_permit_it_had_taken() {
    // An account permit taken and then not returned when the host count refuses
    // would shrink the account's ceiling by one on every refused attempt.
    let slots = RunNowSlots::new(1, 1);
    let bob = slots.try_acquire(&account("bob")).expect("bob");
    let alice = account("alice");
    assert!(slots.try_acquire(&alice).is_err());
    drop(bob);

    assert!(slots.try_acquire(&alice).is_ok());
}

#[test]
fn the_shipped_ceilings_are_a_small_share_of_the_blocking_pool() {
    // tokio's default blocking pool is 512 threads. The host ceiling must stay
    // far below it, and an account's must stay below the host's.
    assert!(HOST_RUN_NOW_SLOTS * 8 <= 512);
    assert!(ACCOUNT_RUN_NOW_SLOTS < HOST_RUN_NOW_SLOTS);
    assert!(ACCOUNT_RUN_NOW_SLOTS >= 1);
}
