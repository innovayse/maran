//! Suspending a whole account's cron, and the customer's switch it must not
//! touch.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::list_cron_entries::list_cron_entries;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, SECOND_ID, account, command, distro, entry_id, every_five_minutes,
    schedule,
};
use crate::cron::set_account_cron_suspended::set_account_cron_suspended;
use crate::cron::set_cron_entry_enabled::set_cron_entry_enabled;

/// The line that follows the marker for `id` in `text`.
fn entry_line_of(text: &str, id: &str) -> String {
    let lines: Vec<&str> = text.lines().collect();
    let marker = format!("# maran-entry: {id}");
    let at = lines
        .iter()
        .position(|line| *line == marker)
        .expect("the marker is in the table");

    (*lines.get(at + 1).expect("a line after the marker")).to_owned()
}

/// Creates two entries, the second one disabled by the CUSTOMER.
fn two_entries_one_customer_disabled() -> RecordingCronHost {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
    )
    .expect("the first entry is created");
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("7", "*", "*", "*", "*"),
        &command("echo two"),
    )
    .expect("the second entry is created");
    set_cron_entry_enabled(&host, distro(), &account(), &entry_id(SECOND_ID), false)
        .expect("the customer turns their second entry off");

    host
}

#[test]
fn suspending_the_account_hides_every_managed_entry_from_cron() {
    let host = two_entries_one_customer_disabled();

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let table = host.crontab().expect("a table was installed");
    assert!(
        entry_line_of(&table, FIRST_ID).starts_with("#susp# "),
        "cron must read the enabled entry's line as a comment: {table}"
    );
    assert!(
        entry_line_of(&table, SECOND_ID).starts_with("#susp# "),
        "and the disabled one's too: {table}"
    );
}

#[test]
fn suspending_the_account_does_not_touch_the_entry_the_customer_disabled() {
    // The reversal law, and the reason cron needed a marker of its own: if
    // suspension wrote to the customer's flag, this line would be
    // indistinguishable from one suspension turned off, and the resume below
    // would hand the customer back a job they had switched off themselves.
    let host = two_entries_one_customer_disabled();

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let table = host.crontab().expect("a table was installed");
    assert_eq!(
        entry_line_of(&table, SECOND_ID)
            .strip_prefix("#susp# ")
            .map(|rest| rest.starts_with("#off# ")),
        Some(true),
        "the customer's own prefix must still be under the suspension's: {table}"
    );
    assert!(
        !entry_line_of(&table, FIRST_ID).contains("#off# "),
        "and the entry they left running must not have acquired one: {table}"
    );
}

#[test]
fn resuming_gives_back_exactly_the_entries_the_customer_had_running() {
    let host = two_entries_one_customer_disabled();
    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    set_account_cron_suspended(&host, distro(), &account(), false).expect("resumed");

    let entries = list_cron_entries(&host, &account()).expect("listed");
    let first = entries
        .iter()
        .find(|entry| entry.id.as_str() == FIRST_ID)
        .expect("the first entry survives");
    let second = entries
        .iter()
        .find(|entry| entry.id.as_str() == SECOND_ID)
        .expect("the second entry survives");

    assert!(first.enabled, "the running entry runs again");
    assert!(!first.suspended);
    assert!(
        !second.enabled,
        "the entry the customer turned off must STAY off"
    );
    assert!(!second.suspended);
}

#[test]
fn a_suspended_account_keeps_every_file_and_id_its_entries_own() {
    // The same argument the per-entry disable makes: an entry removed from the
    // table is one the panel can no longer list, resume or delete, with orphan
    // files under a customer's home and nothing naming them.
    let host = two_entries_one_customer_disabled();
    let before = host.file_paths();

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let table = host.crontab().expect("a table was installed");
    assert!(table.contains(FIRST_ID) && table.contains(SECOND_ID));
    assert_eq!(host.file_paths(), before, "no entry file may be touched");
}

#[test]
fn suspending_twice_installs_a_table_that_says_what_the_last_one_said() {
    let host = two_entries_one_customer_disabled();

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");
    let once = host.crontab().expect("a table was installed");
    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended again");

    assert_eq!(host.crontab(), Some(once));
}

#[test]
fn a_line_the_agent_did_not_write_is_carried_across_untouched() {
    // A crontab is not this agent's file. The alternative — rewriting or
    // deleting a line an account added by hand — destroys work nobody asked
    // this agent to destroy; `inspect_account_cron` reports the count instead.
    let host = RecordingCronHost::with_crontab("PATH=/opt/bin\n*/2 * * * * /opt/bin/theirs\n");

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let table = host.crontab().expect("a table was installed");
    assert!(
        table.contains("*/2 * * * * /opt/bin/theirs"),
        "the foreign entry must come back verbatim: {table}"
    );
    assert!(
        !table.contains("#susp# */2"),
        "and it must not be marked as though the panel owned it: {table}"
    );
}

#[test]
fn an_account_with_no_crontab_still_records_the_suspension() {
    // The one thing the per-line prefixes cannot do. Without a table there is
    // no line to mark, so nothing would carry the account's state — and the
    // first entry created while it was suspended would start firing.
    let host = RecordingCronHost::new();

    set_account_cron_suspended(&host, distro(), &account(), true).expect("suspended");

    let table = host.crontab().expect("a table must be installed anyway");
    assert!(
        table.contains("# maran-suspended"),
        "the account's suspension must survive an empty crontab: {table}"
    );

    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo late"),
    )
    .expect("the entry is created");

    let after = host.crontab().expect("a table was installed");
    assert!(
        entry_line_of(&after, FIRST_ID).starts_with("#susp# "),
        "an entry created while the account is suspended must not fire: {after}"
    );
}

#[test]
fn a_refused_install_leaves_the_account_the_way_it_was() {
    let host = two_entries_one_customer_disabled();
    let before = host.crontab().expect("a table was installed");
    host.refuse_install_with(1);

    let refused = set_account_cron_suspended(&host, distro(), &account(), true);

    assert!(matches!(refused, Err(CronError::CrontabRefused { .. })));
    assert_eq!(host.crontab(), Some(before));
}
