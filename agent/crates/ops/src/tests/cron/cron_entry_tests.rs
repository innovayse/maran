//! The one format both sides of the command file agree on.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use crate::cron::model::cron_entry::CronEntry;
use crate::cron::recording_cron_host::command;

/// The file is the command and exactly one newline.
#[test]
fn a_command_file_is_the_command_and_one_newline() {
    assert_eq!(
        CronEntry::file_contents(&command("echo hi")),
        "echo hi\n".to_owned()
    );
}

/// The bytes a `%` and a `#` are written as are the bytes that come back.
#[test]
fn a_command_carrying_a_percent_or_a_hash_survives_the_round_trip() {
    // The two characters that disproved the earlier designs on a real host.
    // They are legal here precisely because this file is not a crontab line.
    let text = "printf '%s\\n' hi # done";

    let written = CronEntry::file_contents(&command(text));

    assert_eq!(CronEntry::command_from_file(&written), text);
}

/// Only the newline the format added is taken back off.
#[test]
fn reading_a_command_file_strips_one_newline_and_no_other_whitespace() {
    // `trim_end` would report a command that is not the one in the file, which
    // for a duplicate check is a match that should not be one.
    assert_eq!(CronEntry::command_from_file("echo hi \n"), "echo hi ");
    assert_eq!(CronEntry::command_from_file("echo hi\n\n"), "echo hi\n");
}

/// A file with no terminator still reads as the command it holds.
#[test]
fn a_command_file_with_no_final_newline_reads_as_its_whole_content() {
    assert_eq!(CronEntry::command_from_file("echo hi"), "echo hi");
}
