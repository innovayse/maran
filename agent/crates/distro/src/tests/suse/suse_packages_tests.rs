//! Tests for the SUSE family's package names, its reading of a failed `zypper install`, of the
//! pending updates and of the reboot probe.
//!
//! Every output below is CONSTRUCTED from `man zypper` and from how the tool's messages are
//! documented to read; none is a recording. The recording on a real openSUSE Leap container is
//! owed (`tests/fixtures/package_manager/` holds nothing for this family, on purpose: a fixture
//! there must be bytes the tool printed). These tests therefore pin what the readers claim, not
//! what `zypper` prints; the readers fail closed on any shape they do not know, so a layout that
//! differs shows up as a refused reading on the first real run and is then recorded.

#![allow(clippy::unwrap_used, clippy::expect_used, clippy::panic)]

use super::{
    PENDING_UPDATES_ARGUMENTS, REBOOT_PROBE_ARGUMENTS, SECURITY_PENDING_UPDATES_ARGUMENTS,
    classify_package_failure, mark_security_updates, package_manager, php_package,
    read_pending_updates, read_reboot_probe_run,
};
use crate::{
    PackageFailure, PendingUpdate, PendingUpdatesFailure, RebootProbeFailure, RebootRequirement,
    UpdateClassification,
};

/// An install against a repository whose server answered its metadata with 404.
const SOURCE_REFUSED: &str = "\
Retrieving repository 'Main Update Repository' metadata ...........[error]
Repository 'Main Update Repository' is invalid.
Download (curl) error for 'https://download.example/update/repodata/repomd.xml':
Error code: HTTP response: 404
Error message: Server returned an error: 'HTTP/1.1 404 Not Found'
";

/// An install on a host whose resolver answered nothing.
const SOURCE_UNREACHABLE: &str = "\
Download (curl) error for 'https://download.example/update/repodata/repomd.xml':
Error code: Connection failed
Error message: Could not resolve host: download.example
";

/// An install whose server refused the TCP connection.
const CONNECTION_REFUSED: &str = "\
Download (curl) error for 'https://download.example/update/repodata/repomd.xml':
Error message: Failed to connect to download.example port 443 after 3 ms: Connection refused
";

/// An install of a name no repository carries, in the form `zypper install` prints.
const PACKAGE_MISSING: &str = "\
Loading repository data...
Reading installed packages...
Package 'nosuchpackage' not found.
";

/// The same, in the form printed when the name is also not a capability.
const PROVIDER_MISSING: &str = "\
Loading repository data...
Reading installed packages...
No provider of 'nosuchpackage' found.
";

#[test]
fn a_repository_answering_with_an_http_error_is_a_refused_source() {
    assert_eq!(
        classify_package_failure(4, SOURCE_REFUSED),
        PackageFailure::SourceRefused
    );
}

#[test]
fn a_mirror_that_does_not_resolve_is_an_unreachable_source() {
    assert_eq!(
        classify_package_failure(4, SOURCE_UNREACHABLE),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_connection_the_mirror_refused_is_an_unreachable_source_and_not_a_refusal() {
    // A refused TCP connection is a source nobody reached: nothing answered
    // the request, so it is not the HTTP refusal the other verdict means.
    assert_eq!(
        classify_package_failure(106, CONNECTION_REFUSED),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_package_no_repository_carries_is_not_available() {
    assert_eq!(
        classify_package_failure(104, PACKAGE_MISSING),
        PackageFailure::NotAvailable
    );
}

#[test]
fn a_name_nothing_provides_is_not_available() {
    assert_eq!(
        classify_package_failure(104, PROVIDER_MISSING),
        PackageFailure::NotAvailable
    );
}

#[test]
fn a_missing_package_beside_an_unreachable_repository_stays_the_sources_fault() {
    // Inverse control: a repository that could not be reached may be the
    // reason the package was not found.
    let both = format!("{SOURCE_UNREACHABLE}{PACKAGE_MISSING}");

    assert_eq!(
        classify_package_failure(106, &both),
        PackageFailure::SourceUnreachable
    );
}

#[test]
fn a_refusal_outweighs_an_unreachable_source_in_the_same_run() {
    let mixed = format!("{SOURCE_UNREACHABLE}{SOURCE_REFUSED}");

    assert_eq!(
        classify_package_failure(4, &mixed),
        PackageFailure::SourceRefused
    );
}

#[test]
fn output_is_not_read_as_zyppers_when_it_did_not_exit_with_an_error_status() {
    // 7 is a held lock, 8 a failed transaction, 107 a failed scriptlet, 100 and 0 are not failures
    // at all: none of them proves a source was at fault.
    for status in [-1, 0, 1, 7, 8, 100, 107] {
        for output in [SOURCE_REFUSED, SOURCE_UNREACHABLE, PACKAGE_MISSING] {
            assert_eq!(
                classify_package_failure(status, output),
                PackageFailure::Local,
                "status {status}"
            );
        }
    }
}

#[test]
fn an_http_line_without_a_three_digit_status_is_not_a_refusal() {
    // The inverse control for the refusal verdict.
    let output = "Error message: Server returned an error: 'HTTP/1.1 unknown'\n";

    assert_eq!(classify_package_failure(4, output), PackageFailure::Local);
}

#[test]
fn a_sentence_that_only_resembles_the_missing_package_one_is_local() {
    // The inverse control for the not-available verdict.
    let text = "Package 'nosuchpackage' is not installed.\n";

    assert_eq!(classify_package_failure(104, text), PackageFailure::Local);
}

#[test]
fn a_run_with_no_failure_marker_at_all_is_local() {
    assert_eq!(
        classify_package_failure(4, "Some other complaint.\n"),
        PackageFailure::Local
    );
}

#[test]
fn the_package_manager_is_zypper() {
    assert_eq!(package_manager(), "/usr/bin/zypper");
}

#[test]
fn the_distributions_own_php_minor_is_the_unversioned_php8_package() {
    assert_eq!(php_package("8.4"), "php8-fpm");
}

#[test]
fn every_other_php_version_names_a_package_no_repository_carries() {
    for version in ["7.4", "8.0", "8.1", "8.2", "8.3"] {
        let package = php_package(version);

        assert_eq!(package, format!("php{version}-fpm"));
        assert_ne!(package, php_package("8.4"), "{version}");
    }
}

// The pending-updates tests read a table constructed from `man zypper`: two progress sentences,
// the header, a rule and one row per update. The repository cell holds spaces because zypper
// prints a repository by its name.

/// The progress sentences zypper prints before any table.
const PREAMBLE: &str = "Loading repository data...\nReading installed packages...\n";

/// The header and rule of the `list-updates` table.
const UPDATES_TABLE_HEAD: &str = "\
S | Repository             | Name     | Current Version | Available Version | Arch
--+------------------------+----------+-----------------+-------------------+-------
";

/// Three updates from two repositories.
const WITH_UPDATES: &str = "\
Loading repository data...
Reading installed packages...
S | Repository             | Name     | Current Version | Available Version | Arch
--+------------------------+----------+-----------------+-------------------+-------
v | Main Update Repository | curl     | 8.6.0-1.1       | 8.6.0-2.1         | x86_64
v | Main Update Repository | libcurl4 | 8.6.0-1.1       | 8.6.0-2.1         | x86_64
v | Main Repository        | php8-fpm | 8.4.10-1.1      | 8.4.16-1.1        | x86_64
";

/// What zypper prints when nothing is pending.
const UP_TO_DATE: &str = "\
Loading repository data...
Reading installed packages...
No updates found.
";

/// The update named `name` in a read, or a panic naming the package.
fn row<'a>(updates: &'a [PendingUpdate], name: &str) -> &'a PendingUpdate {
    updates
        .iter()
        .find(|update| update.package_name == name)
        .unwrap_or_else(|| panic!("no row for {name}"))
}

/// `WITH_UPDATES` with `extra` appended as its last line, and the one-based number of that line.
fn with_line(extra: &str) -> (String, usize) {
    let output = format!("{WITH_UPDATES}{extra}\n");
    let line = output.lines().count();
    (output, line)
}

#[test]
fn the_pending_updates_run_is_cache_only_with_no_operand() {
    assert_eq!(
        PENDING_UPDATES_ARGUMENTS,
        &["--non-interactive", "--no-refresh", "list-updates"]
    );
}

#[test]
fn every_row_of_the_table_is_an_update() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(updates.len(), 3);
}

#[test]
fn a_row_carries_both_versions_the_repository_name_and_no_classification() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(
        row(&updates, "php8-fpm"),
        &PendingUpdate {
            package_name: String::from("php8-fpm"),
            available_version: String::from("8.4.16-1.1"),
            installed_version: Some(String::from("8.4.10-1.1")),
            source: String::from("Main Repository"),
            classification: UpdateClassification::Unmeasured,
        }
    );
}

#[test]
fn a_repository_name_with_spaces_stays_whole() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert_eq!(row(&updates, "curl").source, "Main Update Repository");
}

#[test]
fn status_one_hundred_is_a_success_too() {
    assert_eq!(read_pending_updates(100, WITH_UPDATES).unwrap().len(), 3);
}

#[test]
fn a_two_character_and_an_empty_status_cell_are_both_rows() {
    let extra = "i+ | Repo | one | 1 | 2 | noarch\n | Repo | two | 1 | 2 | noarch";
    let (output, _) = with_line(extra);

    let updates = read_pending_updates(0, &output).unwrap();

    assert_eq!(updates.len(), 5);
}

#[test]
fn the_sentence_that_nothing_is_pending_is_an_up_to_date_host() {
    assert_eq!(read_pending_updates(0, UP_TO_DATE), Ok(Vec::new()));
}

#[test]
fn no_status_but_zero_and_one_hundred_is_a_success() {
    for status in [-1, 1, 4, 5, 6, 7, 8, 101, 104, 106, 255] {
        assert_eq!(
            read_pending_updates(status, WITH_UPDATES),
            Err(PendingUpdatesFailure::Unsuccessful { status }),
            "status {status}"
        );
    }
}

#[test]
fn a_skipped_repository_is_a_failure_and_not_an_empty_list() {
    // Exit 106 is "a repository was skipped", the shape of a cache that was never built: the
    // listing may be partial, so it is never read.
    assert_eq!(
        read_pending_updates(106, UP_TO_DATE),
        Err(PendingUpdatesFailure::Unsuccessful { status: 106 })
    );
}

#[test]
fn empty_output_under_status_zero_is_incomplete_and_never_an_up_to_date_host() {
    assert_eq!(
        read_pending_updates(0, ""),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
    assert_eq!(
        read_pending_updates(0, PREAMBLE),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn a_header_with_no_row_is_incomplete_and_never_an_up_to_date_host() {
    let output = format!("{PREAMBLE}{UPDATES_TABLE_HEAD}");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn status_one_hundred_with_no_row_is_incomplete_and_not_an_empty_list() {
    assert_eq!(
        read_pending_updates(100, UP_TO_DATE),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn rows_beside_the_sentence_that_nothing_is_pending_are_incomplete() {
    let output = format!("{WITH_UPDATES}No updates found.\n");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::IncompleteOutput)
    );
}

#[test]
fn a_row_with_five_cells_fails_the_whole_read_with_its_number() {
    let (output, line) = with_line("v | Main Repository | short | 1 | 2");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_with_seven_cells_fails_the_whole_read() {
    let (output, line) = with_line("v | Main Repository | long | 1 | 2 | x86_64 | surprise");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_name_with_a_space_fails_the_whole_read() {
    let (output, line) = with_line("v | Main Repository | bad name | 1 | 2 | x86_64");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_version_with_a_control_character_fails_the_whole_read() {
    let (output, line) = with_line("v | Main Repository | pkg | 1\u{7} | 2 | x86_64");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_repository_name_outside_printable_ascii_fails_the_whole_read() {
    let (output, line) = with_line("v | Rep\u{e9}pot | pkg | 1 | 2 | x86_64");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_status_cell_of_three_characters_fails_the_whole_read() {
    let (output, line) = with_line("vvv | Main Repository | pkg | 1 | 2 | x86_64");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_row_before_the_header_fails_the_read_at_its_line() {
    assert_eq!(
        read_pending_updates(0, "v | Main Repository | pkg | 1 | 2 | x86_64\n"),
        Err(PendingUpdatesFailure::UnrecognisedLine { line: 1 })
    );
}

#[test]
fn a_rule_before_the_header_fails_the_read_at_its_line() {
    assert_eq!(
        read_pending_updates(0, "--+------\n"),
        Err(PendingUpdatesFailure::UnrecognisedLine { line: 1 })
    );
}

#[test]
fn a_second_header_fails_the_read() {
    let header = UPDATES_TABLE_HEAD.lines().next().unwrap();
    let (output, line) = with_line(header);

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn a_sentence_inside_the_table_fails_the_read() {
    let (output, line) = with_line("Warning: something else.");

    assert_eq!(
        read_pending_updates(0, &output),
        Err(PendingUpdatesFailure::UnrecognisedLine { line })
    );
}

#[test]
fn the_security_run_is_cache_only_and_asks_for_the_security_category() {
    assert_eq!(
        SECURITY_PENDING_UPDATES_ARGUMENTS,
        &[
            "--non-interactive",
            "--no-refresh",
            "list-patches",
            "--category",
            "security",
        ]
    );
}

/// A patch table naming one pending security patch: the name is an advisory, not a package.
const SECURITY_PATCHES: &str = "\
Loading repository data...
Reading installed packages...
Repository | Name | Category | Severity | Interactive | Status | Since | Summary
-----------+------+----------+----------+-------------+--------+-------+--------
Updates    | openSUSE-Leap-16.0-2026-123 | security | important | --- | needed | - | curl
";

#[test]
fn the_first_reading_leaves_every_row_unmeasured() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    assert!(
        updates
            .iter()
            .all(|update| update.classification == UpdateClassification::Unmeasured)
    );
}

#[test]
fn a_patch_table_that_names_no_package_leaves_every_row_unmeasured() {
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let marked = mark_security_updates(updates.clone(), 0, SECURITY_PATCHES).unwrap();

    assert_eq!(marked, updates);
}

#[test]
fn a_patch_reading_with_nothing_to_name_never_clears_a_row_to_ordinary() {
    // A repository without patch metadata prints the same empty answer as a host with no pending
    // security patch, so the empty reading proves nothing about any row.
    let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

    let marked = mark_security_updates(updates, 0, UP_TO_DATE).unwrap();

    assert_eq!(marked.len(), 3);
    assert!(
        marked
            .iter()
            .all(|update| update.classification == UpdateClassification::Unmeasured)
    );
}

#[test]
fn the_statuses_the_patch_commands_end_on_are_accepted() {
    for status in [0, 100, 101] {
        let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

        assert_eq!(
            mark_security_updates(updates, status, "").unwrap().len(),
            3,
            "status {status}"
        );
    }
}

#[test]
fn marking_no_rows_is_an_empty_answer() {
    assert_eq!(
        mark_security_updates(Vec::new(), 0, SECURITY_PATCHES).unwrap(),
        Vec::new()
    );
}

#[test]
fn a_failed_patch_reading_is_an_error_and_never_an_ordinary_verdict() {
    for status in [-1, 1, 4, 6, 7, 102, 104, 106] {
        let updates = read_pending_updates(0, WITH_UPDATES).unwrap();

        assert_eq!(
            mark_security_updates(updates, status, SECURITY_PATCHES),
            Err(PendingUpdatesFailure::Unsuccessful { status }),
            "status {status}"
        );
    }
}

#[test]
fn the_reboot_probe_run_takes_no_operand() {
    assert_eq!(REBOOT_PROBE_ARGUMENTS, &["needs-rebooting"]);
}

#[test]
fn needs_rebooting_exiting_zero_means_no_reboot_is_required() {
    assert_eq!(read_reboot_probe_run(0), Ok(RebootRequirement::NotRequired));
}

#[test]
fn needs_rebooting_exiting_one_hundred_and_two_means_a_reboot_is_required() {
    assert_eq!(read_reboot_probe_run(102), Ok(RebootRequirement::Required));
}

#[test]
fn any_other_needs_rebooting_status_is_an_error_and_never_not_required() {
    // 127 is a shell's answer for a program that is not installed.
    for status in [1, 100, 101, 103, 127, 255, -1] {
        assert_eq!(
            read_reboot_probe_run(status),
            Err(RebootProbeFailure::Unsuccessful { status })
        );
    }
}
