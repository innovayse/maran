//! Tests for the entry message a cron listing puts on the wire.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use maran_agent_core::validation::system::cron_entry_id::CronEntryId;
use maran_agent_core::validation::system::cron_schedule::CronSchedule;
use maran_ops::cron::CronEntry;

use super::listed_entry;

/// An id of the shape this agent mints.
const ENTRY_ID: &str = "9f1c2d3e-4a5b-4c6d-8e9f-0a1b2c3d4e5f";

/// One entry with `command`, at a schedule whose five fields all differ.
///
/// All five differ on purpose: a mapping that read the hour into the minute
/// field would still pass against `* * * * *`.
fn entry(command: Option<&str>) -> CronEntry {
    CronEntry {
        id: CronEntryId::parse(ENTRY_ID).expect("a valid entry id"),
        schedule: CronSchedule::parse("5", "6", "7", "8", "3").expect("a valid schedule"),
        enabled: true,
        command: command.map(str::to_owned),
    }
}

#[test]
fn every_schedule_field_reaches_its_own_field_on_the_wire() {
    let wire = listed_entry(entry(Some("echo hello")));
    let schedule = wire.schedule.expect("a listed entry carries its schedule");

    assert_eq!(schedule.minute, "5");
    assert_eq!(schedule.hour, "6");
    assert_eq!(schedule.day_of_month, "7");
    assert_eq!(schedule.month, "8");
    assert_eq!(schedule.day_of_week, "3");
    assert_eq!(wire.entry_id, ENTRY_ID);
    assert_eq!(wire.command, "echo hello");
    assert!(wire.enabled);
}

#[test]
fn a_command_with_a_percent_and_a_hash_reaches_the_wire_untouched() {
    // The two characters that killed two earlier designs. They are ordinary
    // here because the command lives in a file rather than on a crontab line,
    // and this asserts the listing does not quietly re-introduce an escape.
    let wire = listed_entry(entry(Some("date +%s # nightly")));

    assert_eq!(wire.command, "date +%s # nightly");
}

#[test]
fn an_entry_whose_command_file_is_gone_is_still_listed_with_an_empty_command() {
    // The alternative — dropping it from the listing — hides an entry cron is
    // still running, which is the worse of the two answers. This goes red if
    // somebody makes a missing file skip the entry or raise an error.
    let wire = listed_entry(entry(None));

    assert_eq!(wire.entry_id, ENTRY_ID);
    assert_eq!(wire.command, "");
}

#[test]
fn a_listing_reports_the_run_fields_as_unread_rather_than_as_a_successful_run() {
    // `cron.proto` says both are unproduced by a listing. The assertion exists
    // because 0 is also the exit status of a run that succeeded: a panel that
    // drew a green tick from this field would be reporting on a job that may
    // never have started.
    let wire = listed_entry(entry(Some("true")));

    assert_eq!(wire.last_exit_code, 0);
    assert_eq!(wire.last_run_at_unix, 0);
}
