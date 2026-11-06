//! Tests for `audit_slot`: what a handler can put in the slot and how it degrades.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use super::AuditSlot;
use crate::audit::audit_outcome::AuditOutcome;
use crate::proto::{AgentError, ErrorCode};

/// An agent error carrying `code` as the wire does.
fn error_with(code: i32) -> AgentError {
    AgentError {
        code,
        ..AgentError::default()
    }
}

#[test]
fn a_detached_slot_absorbs_every_call() {
    let slot = AuditSlot::of(&http::Extensions::new());

    slot.account(&AccountName::parse("alice_01").unwrap());
    slot.site(&Domain::parse("example.com").unwrap());
    slot.ok();
    slot.refused(ErrorCode::NotFound);
    slot.settle::<()>(&Err(error_with(ErrorCode::NotFound as i32)));

    assert_eq!(
        slot.settled_outcome(),
        Some(AuditOutcome::Refused(ErrorCode::NotFound))
    );
}

#[test]
fn two_detached_slots_share_nothing() {
    let extensions = http::Extensions::new();
    AuditSlot::of(&extensions).ok();

    assert_eq!(AuditSlot::of(&extensions).settled_outcome(), None);
}

#[test]
fn an_installed_slot_is_the_one_a_handler_finds() {
    let mut extensions = http::Extensions::new();
    let installed = AuditSlot::install(&mut extensions);

    AuditSlot::of(&extensions).ok();

    assert_eq!(installed.settled_outcome(), Some(AuditOutcome::Ok));
}

#[test]
fn settling_an_ok_result_records_ok() {
    let slot = AuditSlot::of(&http::Extensions::new());

    slot.settle(&Ok::<_, AgentError>(()));

    assert_eq!(slot.settled_outcome(), Some(AuditOutcome::Ok));
}

#[test]
fn settling_an_error_records_its_code() {
    let slot = AuditSlot::of(&http::Extensions::new());

    slot.settle::<()>(&Err(error_with(ErrorCode::AlreadyExists as i32)));

    assert_eq!(
        slot.settled_outcome(),
        Some(AuditOutcome::Refused(ErrorCode::AlreadyExists))
    );
}

#[test]
fn settling_an_error_with_an_unknown_code_records_unspecified() {
    let slot = AuditSlot::of(&http::Extensions::new());

    slot.settle::<()>(&Err(error_with(9999)));

    assert_eq!(
        slot.settled_outcome(),
        Some(AuditOutcome::Refused(ErrorCode::Unspecified))
    );
}

#[test]
fn account_and_site_are_recorded_and_overwritten_not_accumulated() {
    let slot = AuditSlot::of(&http::Extensions::new());

    slot.account(&AccountName::parse("alice_01").unwrap());
    slot.account(&AccountName::parse("bob_02").unwrap());
    slot.site(&Domain::parse("example.com").unwrap());

    assert_eq!(slot.recorded_account().unwrap().as_str(), "bob_02");
    assert_eq!(slot.recorded_site().unwrap().as_str(), "example.com");
}

#[test]
fn a_fresh_slot_has_nothing_settled_or_recorded() {
    let slot = AuditSlot::of(&http::Extensions::new());

    assert_eq!(slot.settled_outcome(), None);
    assert_eq!(slot.recorded_account(), None);
    assert_eq!(slot.recorded_site(), None);
}

#[test]
fn a_poisoned_mutex_degrades_without_panicking() {
    let slot = AuditSlot::of(&http::Extensions::new());
    slot.account(&AccountName::parse("alice_01").unwrap());
    let poisoner = slot.clone();
    let result = std::thread::spawn(move || {
        let _held = poisoner.record.lock().unwrap();
        panic!("poison the slot on purpose");
    })
    .join();
    assert!(result.is_err(), "the poisoning thread did panic");
    assert!(
        slot.record.is_poisoned(),
        "positive control: it is poisoned"
    );

    slot.ok();
    slot.refused(ErrorCode::NotFound);
    slot.account(&AccountName::parse("bob_02").unwrap());
    slot.site(&Domain::parse("example.com").unwrap());

    assert_eq!(slot.settled_outcome(), None);
    assert_eq!(slot.recorded_account(), None);
    assert_eq!(slot.recorded_site(), None);
}
