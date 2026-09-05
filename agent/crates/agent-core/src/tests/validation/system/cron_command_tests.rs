//! Tests for the `cron_command` module.
//!
//! The happy path is the least interesting test here. What matters is the exact
//! shape of the alphabet: this type deliberately PERMITS two characters an
//! earlier design refused, and refusing them again would break working
//! commands that a real host has already proved out.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{CronCommand, CronCommandError, MAX_LENGTH};

#[test]
fn an_ordinary_command_parses_and_is_kept_verbatim() {
    let command = CronCommand::parse("/usr/bin/php /home/acme/cron.php --now").unwrap();

    assert_eq!(command.as_str(), "/usr/bin/php /home/acme/cron.php --now");
}

#[test]
fn percent_and_hash_are_legal_because_the_command_lives_in_a_file() {
    // The two commands that disproved the earlier in-line design on a real
    // host: cron rewrites the first unescaped `%` on a crontab LINE into a
    // newline, and `#` starts a comment on one. Neither applies to a file.
    for candidate in [
        "date +%s > /tmp/stamp",
        "echo hi # comment",
        "printf '%d%%\\n' 42",
    ] {
        assert_eq!(CronCommand::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn shell_metacharacters_are_legal_because_a_shell_is_what_runs_the_file() {
    for candidate in [
        "ls; echo done",
        "a && b || c",
        "cat file | grep x > out 2>&1",
        "echo $HOME `date`",
    ] {
        assert_eq!(CronCommand::parse(candidate).unwrap().as_str(), candidate);
    }
}

#[test]
fn control_characters_are_refused_one_by_one() {
    for character in ['\n', '\r', '\t', '\u{0}', '\u{7}', '\u{1b}'] {
        assert_eq!(
            CronCommand::parse(&format!("echo{character}hi")),
            Err(CronCommandError::ControlCharacter { character })
        );
    }
}

#[test]
fn the_length_ceiling_is_enforced() {
    let longest = "e".repeat(MAX_LENGTH);
    assert_eq!(CronCommand::parse(&longest).unwrap().as_str(), longest);

    let overlong = "e".repeat(MAX_LENGTH + 1);
    assert_eq!(
        CronCommand::parse(&overlong),
        Err(CronCommandError::TooLong {
            maximum: MAX_LENGTH
        })
    );
}

#[test]
fn surrounding_whitespace_is_refused() {
    for candidate in [" ls", "ls ", "  ls  "] {
        assert_eq!(
            CronCommand::parse(candidate),
            Err(CronCommandError::SurroundingWhitespace)
        );
    }
}

#[test]
fn whitespace_inside_the_command_is_kept() {
    assert_eq!(CronCommand::parse("ls  -l").unwrap().as_str(), "ls  -l");
}

#[test]
fn an_empty_command_is_refused() {
    assert_eq!(CronCommand::parse(""), Err(CronCommandError::Empty));
}
