//! Tests for how the SUSE family reads which packages its repositories offer.
//!
//! The outputs are CONSTRUCTED from `man zypper` (`search --details --match-exact -t package`):
//! two progress sentences, a header, a rule, and one `S | Name | Type | Version | Arch |
//! Repository` row per match. None is a recording; the recording on a real openSUSE Leap container
//! is owed, and `tests/fixtures/package_manager/` holds nothing for this family on purpose, because
//! a fixture there must be bytes the tool printed. These tests pin what the reader claims, and the
//! reader fails closed on any shape it does not know.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{package_candidate_arguments, package_candidate_binary, read_package_candidates};
use crate::PackageQueryFailure;

/// The progress sentences and the header and rule of the `search --details` table.
const TABLE_HEAD: &str = "\
Loading repository data...
Reading installed packages...

S  | Name     | Type    | Version    | Arch   | Repository
---+----------+---------+------------+--------+----------------
";

/// Two rows: one name that was asked about and one that was not.
const MIXED: &str = "\
Loading repository data...
Reading installed packages...

S  | Name     | Type    | Version    | Arch   | Repository
---+----------+---------+------------+--------+----------------
   | php8-fpm | package | 8.4.16-1.1 | x86_64 | Main Repository
i+ | php8-gd  | package | 8.4.16-1.1 | x86_64 | Main Repository
";

/// What zypper printed on standard output when nothing matched.
const NO_MATCH: &str = "\
Loading repository data...
Reading installed packages...
No matching items found.
";

/// The two names the sample asked about: one carried by the repositories, one not.
fn asked() -> Vec<String> {
    vec!["php8-fpm".to_owned(), "php8.3-fpm".to_owned()]
}

/// `MIXED` with `extra` appended as its last line, and the one-based number of that line.
fn with_line(extra: &str) -> (String, usize) {
    let output = format!("{MIXED}{extra}\n");
    let line = output.lines().count();
    (output, line)
}

#[test]
fn only_the_name_zypper_listed_is_offered() {
    let offered = read_package_candidates(0, MIXED, "", &asked()).unwrap();

    assert_eq!(offered, vec!["php8-fpm".to_owned()]);
}

#[test]
fn a_row_for_a_name_that_was_not_asked_about_is_never_offered() {
    let offered = read_package_candidates(0, MIXED, "", &["php8.3-fpm".to_owned()]).unwrap();

    assert!(offered.is_empty());
}

#[test]
fn a_package_two_repositories_carry_is_offered_once() {
    let (output, _) = with_line("   | php8-fpm | package | 8.4.15 | x86_64 | Updates");

    let offered = read_package_candidates(0, &output, "", &asked()).unwrap();

    assert_eq!(offered, vec!["php8-fpm".to_owned()]);
}

#[test]
fn the_answer_follows_the_order_the_names_were_asked_in() {
    let names = vec!["php8-gd".to_owned(), "php8-fpm".to_owned()];

    let offered = read_package_candidates(0, MIXED, "", &names).unwrap();

    assert_eq!(offered, names);
}

#[test]
fn standard_error_never_changes_the_answer() {
    let warning = "Repository 'Main Repository' is out-dated.\n";

    let offered = read_package_candidates(0, MIXED, warning, &asked()).unwrap();

    assert_eq!(offered, vec!["php8-fpm".to_owned()]);
}

#[test]
fn nothing_matching_is_an_answer_and_offers_nothing() {
    let offered = read_package_candidates(104, NO_MATCH, "", &asked()).unwrap();

    assert!(offered.is_empty());
}

#[test]
fn a_not_found_status_beside_a_row_is_a_failure() {
    // Inverse control to the test above: the same status 104, rows on standard output.
    assert_eq!(
        read_package_candidates(104, MIXED, "", &asked()),
        Err(PackageQueryFailure::Unsuccessful { status: 104 })
    );
}

#[test]
fn a_success_status_with_no_row_is_a_failure_and_never_an_empty_list() {
    for output in ["", NO_MATCH, TABLE_HEAD] {
        assert_eq!(
            read_package_candidates(0, output, "", &asked()),
            Err(PackageQueryFailure::Unsuccessful { status: 0 })
        );
    }
}

#[test]
fn any_status_but_zero_and_one_hundred_and_four_is_a_failure() {
    // 106 is a skipped repository (a cache that was never built), 4 a failed refresh, 7 a lock.
    for status in [-1, 1, 4, 6, 7, 8, 100, 106, 127] {
        assert_eq!(
            read_package_candidates(status, MIXED, "", &asked()),
            Err(PackageQueryFailure::Unsuccessful { status }),
            "status {status}"
        );
    }
}

#[test]
fn a_row_with_five_cells_fails_the_read_with_its_number() {
    let (output, line) = with_line("   | php8-fpm | package | 8.4.16-1.1 | x86_64");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_with_seven_cells_fails_the_read() {
    let (output, line) = with_line("   | php8-fpm | package | 1 | x86_64 | Repo | extra");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_that_is_not_a_package_fails_the_read() {
    let (output, line) = with_line("   | php8-fpm | patch | 1 | x86_64 | Repo");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_name_with_a_space_fails_the_read() {
    let (output, line) = with_line("   | php8 fpm | package | 1 | x86_64 | Repo");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_version_with_a_control_character_fails_the_read() {
    let (output, line) = with_line("   | php8-fpm | package | 1\u{7} | x86_64 | Repo");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_before_the_header_fails_the_read_at_its_line() {
    let output = "   | php8-fpm | package | 8.4.16-1.1 | x86_64 | Main Repository\n";

    assert_eq!(
        read_package_candidates(0, output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line: 1 })
    );
}

#[test]
fn a_rule_before_the_header_fails_the_read_at_its_line() {
    assert_eq!(
        read_package_candidates(0, "---+----\n", "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line: 1 })
    );
}

#[test]
fn a_second_header_fails_the_read() {
    let header = TABLE_HEAD.lines().nth(3).unwrap();
    let (output, line) = with_line(header);

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_sentence_inside_the_table_fails_the_read() {
    let (output, line) = with_line("Warning: something else.");

    assert_eq!(
        read_package_candidates(0, &output, "", &asked()),
        Err(PackageQueryFailure::UnrecognisedLine { line })
    );
}

#[test]
fn the_query_is_a_cache_only_exact_package_search_with_the_operands_after_the_separator() {
    assert_eq!(package_candidate_binary().to_string(), "/usr/bin/zypper");

    assert_eq!(
        package_candidate_arguments(&asked()),
        [
            "--non-interactive",
            "--no-refresh",
            "search",
            "--details",
            "--match-exact",
            "-t",
            "package",
            "--",
            "php8-fpm",
            "php8.3-fpm",
        ]
    );
}

#[test]
fn an_operand_that_looks_like_an_option_stays_after_the_separator() {
    let arguments = package_candidate_arguments(&["--root=/tmp".to_owned()]);

    assert_eq!(arguments[arguments.len() - 2], "--");
    assert_eq!(arguments[arguments.len() - 1], "--root=/tmp");
}
