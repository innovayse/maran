//! What a creation writes, in which order, and what it refuses to do twice.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use std::sync::Arc;
use std::thread;

use crate::cron::create_cron_entry::create_cron_entry;
use crate::cron::cron_error::CronError;
use crate::cron::model::crontab_document::CrontabDocument;
use crate::cron::recording_cron_host::{
    FIRST_ID, RecordingCronHost, SECOND_ID, THIRD_ID, account, cmd_path, command, distro, entry_id,
    every_five_minutes, schedule,
};

/// The command file holds the command exactly, and one newline after it.
#[test]
fn the_command_file_holds_the_command_verbatim_with_one_trailing_newline() {
    // `%` and `#` are ordinary shell text in a file and must survive untouched:
    // they are legal in a command precisely because the command never reaches a
    // crontab line, where cron would rewrite the first and comment out the
    // rest.
    let text = "printf '%s\\n' hi # done";
    let host = RecordingCronHost::new();

    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command(text),
        None,
    )
    .expect("created");

    let written = host
        .file(&cmd_path(&account(), &entry_id(FIRST_ID)))
        .expect("a command file was written");
    assert_eq!(written, format!("{text}\n"));
}

/// A second entry with the same schedule and command is refused before any
/// write.
#[test]
fn creating_an_identical_entry_reports_already_exists_and_writes_nothing() {
    // The comparison reads the `.cmd` files back, because the crontab no longer
    // carries commands. A retry after a lost reply must not leave the customer
    // with one entry they can see and one they cannot explain.
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("created");
    let after_first = host.crontab().expect("a table was installed");

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::AlreadyExists));
    assert_eq!(host.installs().len(), 1, "no second table may be installed");
    assert_eq!(host.file_paths().len(), 1, "no second file may be written");
    assert_eq!(host.crontab(), Some(after_first));
}

/// A disabled twin still counts as the entry the customer already has.
#[test]
fn creating_an_entry_that_matches_a_disabled_one_is_still_a_duplicate() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("created");
    crate::cron::set_cron_entry_enabled::set_cron_entry_enabled(
        &host,
        distro(),
        &account(),
        &entry_id(FIRST_ID),
        false,
    )
    .expect("disabled");

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::AlreadyExists));
}

/// The same command at a different time is a different entry.
#[test]
fn the_same_command_at_another_schedule_is_not_a_duplicate() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("created");

    create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("0", "3", "*", "*", "*"),
        &command("echo one"),
        None,
    )
    .expect("created a second entry");

    assert_eq!(host.file_paths().len(), 2);
}

/// A refused install takes the command file away with it.
#[test]
fn a_failed_install_leaves_no_orphan_command_file() {
    // The entry is not in the crontab, so a file left behind is litter inside
    // the customer's home that nothing will ever run and nothing will ever
    // clean up.
    let host = RecordingCronHost::new();
    host.refuse_install_with(1);

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 1 }));
    assert!(
        host.file_paths().is_empty(),
        "the command file must be removed again: {:?}",
        host.file_paths()
    );
}

/// A creation reports the id it minted, so the caller can name the entry.
#[test]
fn creating_an_entry_reports_the_id_it_minted() {
    let host = RecordingCronHost::new();

    let id = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("created");

    assert_eq!(id, entry_id(FIRST_ID));
}

/// A host that cannot mint an id writes nothing at all.
#[test]
fn an_entry_id_that_cannot_be_minted_stops_the_creation_before_any_write() {
    let host = RecordingCronHost::new();
    host.refuse_ids();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::EntryIdUnavailable));
    assert!(host.installs().is_empty());
    assert!(host.file_paths().is_empty());
}

/// A command file that cannot be written stops the creation before the install.
#[test]
fn a_command_file_that_cannot_be_written_stops_the_creation() {
    let host = RecordingCronHost::new();
    host.refuse_writes();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::EntryFileUnwritable));
    assert!(
        host.installs().is_empty(),
        "no table may be installed for an entry with no command"
    );
}

/// A crontab that cannot be read stops the creation.
#[test]
fn a_crontab_that_cannot_be_read_stops_the_creation() {
    let host = RecordingCronHost::new();
    host.refuse_crontab_read_with(7);

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    );

    assert_eq!(refusal, Err(CronError::CrontabRefused { code: 7 }));
}

/// A command file the duplicate check cannot read is a refusal, not a pass.
#[test]
fn a_command_file_that_cannot_be_read_stops_the_duplicate_check() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("created");
    host.refuse_entry_reads();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo two"),
        None,
    );

    assert_eq!(refusal, Err(CronError::EntryFileUnreadable));
}

/// An account that has already used its whole allowance gets no further entry.
#[test]
fn a_creation_beyond_the_stated_allowance_is_refused_and_writes_nothing() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        Some(1),
    )
    .expect("the first entry is within an allowance of one");
    let after_first = host.crontab().expect("a table was installed");
    let files_after_first = host.file_paths();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("0", "4", "*", "*", "*"),
        &command("echo two"),
        Some(1),
    );

    assert_eq!(refusal, Err(CronError::EntryLimitReached));
    assert_eq!(
        host.crontab().expect("the table is still there"),
        after_first,
        "a refused creation must leave the installed table exactly as it was"
    );
    assert_eq!(
        host.file_paths(),
        files_after_first,
        "a refused creation must write no command file, or the customer's home \
         keeps a file nothing will ever run and nothing will ever clean up"
    );
}

/// An allowance of zero refuses the account's very first entry.
///
/// The case a sentinel could not express. Zero is a real allowance — a plan that
/// permits no scheduled tasks at all — so a design that reserved it to mean "no
/// allowance stated" would silently give such an account an unlimited one, in
/// the permissive direction, for exactly the plan whose limit is tightest.
#[test]
fn an_allowance_of_zero_refuses_the_first_entry() {
    let host = RecordingCronHost::new();

    let refusal = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        Some(0),
    );

    assert_eq!(refusal, Err(CronError::EntryLimitReached));
    assert_eq!(host.crontab(), None, "nothing may be installed");
    assert!(host.file_paths().is_empty(), "nothing may be written");
}

/// INVERSE CONTROL: creations inside the allowance all still succeed.
///
/// A check mutated to refuse everything passes every test that only ever hands
/// it a full account. This one hands it room, twice, and requires both entries
/// in the installed table.
#[test]
fn creations_within_the_stated_allowance_all_succeed() {
    let host = RecordingCronHost::new();

    for (index, text) in ["echo one", "echo two"].into_iter().enumerate() {
        create_cron_entry(
            &host,
            distro(),
            &account(),
            &schedule("0", &index.to_string(), "*", "*", "*"),
            &command(text),
            Some(3),
        )
        .unwrap_or_else(|error| panic!("an entry within the allowance must be installed: {error}"));
    }

    let document = CrontabDocument::parse(&host.crontab().expect("a table was installed"));
    assert_eq!(
        document.entries().len(),
        2,
        "both entries were within an allowance of three and must be in the table"
    );
}

/// INVERSE CONTROL: a request that states no allowance is not refused.
///
/// This is the skew case, driven rather than argued. A caller predating the
/// allowance — an older panel, any other client — sends nothing, and the field's
/// absence must mean "no allowance stated" rather than proto3's zero. Were it
/// read as a number, this account, which already holds an entry, would be
/// refused, and so would every cron entry every such caller ever asked for.
#[test]
fn a_creation_that_states_no_allowance_is_never_refused_for_a_limit() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        None,
    )
    .expect("the first entry is created");

    let second = create_cron_entry(
        &host,
        distro(),
        &account(),
        &schedule("0", "4", "*", "*", "*"),
        &command("echo two"),
        None,
    );

    assert_eq!(second, Ok(entry_id(SECOND_ID)));
    let document = CrontabDocument::parse(&host.crontab().expect("a table was installed"));
    assert_eq!(document.entries().len(), 2);
}

/// A retry of an entry the account already has answers AlreadyExists even when
/// the account is now full.
///
/// The order of the two refusals, asserted rather than commented. The account
/// that has just filled its allowance is exactly the account whose creation
/// succeeded, so the retry after a lost response arrives with the crontab full.
/// Asked in the other order this would answer "your plan is full" about an entry
/// that is already installed and running, and the caller would report a failure
/// for work that had in fact been done — this rpc's idempotency, lost.
#[test]
fn a_retry_of_an_existing_entry_reports_already_exists_rather_than_the_limit() {
    let host = RecordingCronHost::new();
    create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        Some(1),
    )
    .expect("the first entry is within an allowance of one");

    let retry = create_cron_entry(
        &host,
        distro(),
        &account(),
        &every_five_minutes(),
        &command("echo one"),
        Some(1),
    );

    assert_eq!(retry, Err(CronError::AlreadyExists));
}

/// Two creations racing on an account one below its allowance install exactly
/// one entry, and the loser is told it was the limit.
///
/// **This is the race the panel cannot close, driven rather than argued.** The
/// panel counts by asking the agent over one rpc and installs over a second, so
/// two requests interleave between them and both pass a panel-side check. Both
/// callers below are past any such check by construction: each states the same
/// allowance of two against an account already holding one entry, which is
/// exactly what a pre-check would have waved through.
///
/// The fake parks the first caller between its crontab read and its install
/// (`with_arrival_gate`), so the interleaving happens on every run rather than
/// on the runs where the scheduler cooperates.
///
/// # How this test can LOSE
///
/// Remove the allowance comparison and both callers answer `Ok` and the table
/// holds three entries. Remove the per-account lock and both read the same
/// one-entry document, neither sees a full account, and both answer `Ok` again.
/// Each is caught by the first assertion, which is about the pair of outcomes
/// and not about either one alone.
///
/// # The controls
///
/// - **`arrivals == 2`** — both callers really reached the crontab
///   read-modify-write. An assertion about the final table proves nothing if the
///   second operation never started.
/// - **The rendezvous timed out** — the second was held OUTSIDE that section
///   while the first was inside it, which is the lock. The count alone cannot
///   say so: a caller parked on a lock still arrives, one release later.
/// - **The winner's entry is in the final table** — a lock that made the winner
///   lose its write would satisfy "one refusal" and be a different defect.
/// - A spare id is queued, so a build whose refusal stopped happening FAILS this
///   assertion instead of panicking inside the fake for want of an id.
#[test]
fn two_creations_racing_one_below_the_allowance_install_exactly_one() {
    let seeding = RecordingCronHost::new();
    create_cron_entry(
        &seeding,
        distro(),
        &account(),
        &schedule("0", "1", "*", "*", "*"),
        &command("echo seeded"),
        None,
    )
    .expect("the account's first entry is installed");

    let host = Arc::new(
        RecordingCronHost::with_crontab(&seeding.crontab().expect("a table was installed"))
            .with_ids(&[SECOND_ID, THIRD_ID])
            .with_arrival_gate(),
    );

    let racers: Vec<_> = ["echo racer one", "echo racer two"]
        .into_iter()
        .map(|text| {
            let host = Arc::clone(&host);

            thread::spawn(move || {
                create_cron_entry(
                    host.as_ref(),
                    distro(),
                    &account(),
                    &every_five_minutes(),
                    &command(text),
                    Some(2),
                )
            })
        })
        .collect();
    let outcomes: Vec<_> = racers
        .into_iter()
        .map(|racer| racer.join().expect("the racing thread finished"))
        .collect();

    let installed: Vec<_> = outcomes.iter().filter(|outcome| outcome.is_ok()).collect();
    let refused: Vec<_> = outcomes
        .iter()
        .filter(|outcome| **outcome == Err(CronError::EntryLimitReached))
        .collect();

    assert_eq!(
        (installed.len(), refused.len()),
        (1, 1),
        "exactly one of two racing creations may be installed on an account one \
         below its allowance, and the other must be told it was the limit; \
         outcomes were {outcomes:?}"
    );

    assert_eq!(
        host.arrivals(),
        2,
        "both creations must have entered the crontab read-modify-write; one \
         arrival means the race never happened and nothing was measured"
    );
    assert!(
        host.rendezvous_timed_out(),
        "the second creation must have been held outside the read-modify-write \
         while the first was inside it; a completed rendezvous means both were \
         in it at once, which is the race itself"
    );

    let table = host.crontab().expect("a table must be installed");
    let document = CrontabDocument::parse(&table);
    assert_eq!(
        document.entries().len(),
        2,
        "the seeded entry plus the one winner, and no more:\n{table}"
    );

    let winner = installed[0].as_ref().expect("the winner returned an id");
    assert!(
        document.entries().iter().any(|entry| &entry.id == winner),
        "the winning creation answered Ok with an id, so its entry must be in \
         the table:\n{table}"
    );
}
