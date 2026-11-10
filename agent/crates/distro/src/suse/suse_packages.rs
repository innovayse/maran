//! Package and binary names on the SUSE family, how `zypper` reports a failed
//! install, and how it reports the updates it knows of.
//!
//! Nothing in this file was measured by us. Every `zypper` exit status, message and table layout
//! below is read from the distribution's documentation (`man zypper`) and is UNVERIFIED until the
//! first run on the pinned openSUSE Leap image; the readers fail closed on anything they do not
//! know, so a wrong guess shows up as a refused reading and never as a wrong answer. The recordings
//! that would make the tests rest on real bytes are owed (`tests/fixtures/package_manager/` holds
//! none for this family).

use crate::is_printable_token::is_printable_token;
use crate::package_failure::PackageFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
use crate::reboot_probe_failure::RebootProbeFailure;
use crate::reboot_requirement::RebootRequirement;
use crate::starts_with_http_status::starts_with_http_status;
use crate::update_classification::UpdateClassification;

/// The one PHP minor the SUSE family's own repositories ship (openSUSE Leap 16 and SLES 16: 8.4).
///
/// UNVERIFIED: read from the distribution's package listings, confirm with
/// `zypper --non-interactive --no-refresh info php8-fpm` in the pinned Leap image. Shared with
/// [`crate::suse::suse_php_extensions`], which names an extension's package by the same rule.
pub(crate) const DISTRIBUTION_PHP_VERSION: &str = "8.4";

/// Name of the OS package that provides php-fpm for `version`: `php8-fpm` for the one version the
/// distribution ships, and a name no repository carries for every other.
///
/// This family offers ONE PHP minor, from the distribution's own repositories; no third-party
/// repository is added (issue #59, decision D4). The package is not versioned in its name, so the
/// other versions of the panel's supported set cannot be spelled as a real package. They are
/// answered with the dotted, Debian-shaped `php<version>-fpm` on purpose: no SUSE repository
/// carries such a name, so the package-candidate listing, which asks `zypper` what the host
/// offers, leaves those versions out, and the operator is shown only the distribution's own. A
/// forced install of one ends as [`PackageFailure::NotAvailable`]. UNVERIFIED: confirm with
/// `zypper --non-interactive --no-refresh search --match-exact -t package php8.3-fpm` (exit 104
/// is expected).
#[must_use]
pub fn php_package(version: &str) -> String {
    if version == DISTRIBUTION_PHP_VERSION {
        "php8-fpm".to_owned()
    } else {
        format!("php{version}-fpm")
    }
}

/// Absolute path of the package manager binary.
///
/// `zypper` at `/usr/bin/zypper`. UNVERIFIED: confirm with `command -v zypper` in the pinned
/// Leap image.
#[must_use]
pub fn package_manager() -> &'static str {
    "/usr/bin/zypper"
}

/// The exit statuses `zypper` ends a failed package operation on that are its own error reporting:
/// 4 (`ZYPPER_EXIT_ERR_ZYPP`, an error of the libzypp engine, a failed refresh among them), 104
/// (`ZYPPER_EXIT_INF_CAP_NOT_FOUND`, a package or capability nothing provides) and 106
/// (`ZYPPER_EXIT_INF_REPOS_SKIPPED`, a repository was skipped). Any other status (a held lock is 7,
/// a failed transaction 8, a failed scriptlet 107) is the host's own state, and its output is not
/// read as a source's fault. UNVERIFIED: read from `man zypper`, confirm with `man zypper` in the
/// pinned image.
const ZYPPER_FAILURE_STATUSES: [i32; 3] = [4, 104, 106];

/// What `zypper` writes before the HTTP status line of a source that answered with an error, as in
/// `Error message: Server returned an error: 'HTTP/1.1 404 Not Found'`. UNVERIFIED: read from the
/// distribution documentation, confirm by installing from a repository whose server answers 404.
const ZYPPER_HTTP_ERROR: &str = "Server returned an error: 'HTTP/";

/// The libcurl complaints `zypper` quotes for a source it never reached: a name that did not
/// resolve, a connection that failed, was refused or timed out, a network that was down.
/// UNVERIFIED: read from the distribution documentation, confirm by installing with no network.
const ZYPPER_UNREACHABLE_ERRORS: [&str; 7] = [
    "Could not resolve host",
    "Couldn't resolve host",
    "Error code: Connection failed",
    "Failed to connect to",
    "Connection timed out",
    "Operation timed out",
    "Network is unreachable",
];

/// Reads a failed `zypper install` run: its exit `status` and its combined `output`.
///
/// Only a status of 4, 104 or 106 is read at all. A `Server returned an error:
/// 'HTTP/<version> <status> …'` line anywhere in the run is a source that answered and refused,
/// and outweighs unreachable sources elsewhere in it (see [`PackageFailure::SourceRefused`]); a
/// resolve, connect or timeout complaint is a source nobody reached. `Package '<name>' not found.`
/// (exit 104) or `No provider of '<name>' found.` with no source fault beside it is
/// [`PackageFailure::NotAvailable`]; anything else is [`PackageFailure::Local`]. No byte of the
/// output reaches the verdict.
#[must_use]
pub fn classify_package_failure(status: i32, output: &str) -> PackageFailure {
    if !ZYPPER_FAILURE_STATUSES.contains(&status) {
        return PackageFailure::Local;
    }

    let mut verdict = PackageFailure::Local;
    for line in output.lines() {
        let refused = line
            .split_once(ZYPPER_HTTP_ERROR)
            .and_then(|(_, after)| after.split_once(' '))
            .is_some_and(|(_, reason)| starts_with_http_status(reason));
        if refused {
            return PackageFailure::SourceRefused;
        }
        if ZYPPER_UNREACHABLE_ERRORS
            .iter()
            .any(|marker| line.contains(marker))
        {
            verdict = PackageFailure::SourceUnreachable;
        }
    }
    if verdict == PackageFailure::Local && output.lines().any(names_a_missing_package) {
        return PackageFailure::NotAvailable;
    }
    verdict
}

/// Whether `line` is one of the two sentences `zypper install` prints for a name nothing provides,
/// at the start of a line: `Package '<name>' not found.` and `No provider of '<name>' found.`
/// UNVERIFIED: confirm with `zypper --non-interactive install --no-recommends nosuchpackage`.
fn names_a_missing_package(line: &str) -> bool {
    let line = line.trim_end();
    (line.starts_with("Package '") && line.ends_with("' not found."))
        || (line.starts_with("No provider of '") && line.ends_with("' found."))
}

/// The status `zypper` exits with when a command succeeded.
const ZYPPER_OK_STATUS: i32 = 0;

/// The status the update-listing commands are documented to be able to exit with when something is
/// available (`patch-check` uses 100 for "patches available"). `list-updates` is expected to exit
/// 0 in that case too; 100 is accepted so that either reading of the documentation lands on an
/// answer. UNVERIFIED: confirm with `zypper --non-interactive --no-refresh list-updates; echo $?`.
const ZYPPER_UPDATES_STATUS: i32 = 100;

/// The status `zypper patch-check` documents for "security patches available", accepted for the
/// patch listing for the same reason as [`ZYPPER_UPDATES_STATUS`].
const ZYPPER_SECURITY_STATUS: i32 = 101;

/// The status `zypper needs-rebooting` documents for "a reboot is needed"
/// (`ZYPPER_EXIT_INF_REBOOT_NEEDED`).
const ZYPPER_REBOOT_NEEDED_STATUS: i32 = 102;

/// The arguments of the cache-only run that lists pending updates (issue #324).
///
/// `--non-interactive` answers no prompt and `--no-refresh` makes zypper read the metadata already
/// on disk and contact no repository, so the run makes no outbound call and refreshes nothing; they
/// are a constant no caller value reaches. No operand exists, so no `--` is needed. A repository
/// whose metadata was never cached is skipped (status 106) or fails (status 4), both of which
/// [`read_pending_updates`] refuses, so a host that never built its catalogue cannot read as up to
/// date and no separate probe is needed. UNVERIFIED: confirm with
/// `zypper --non-interactive --no-refresh list-updates` in the pinned Leap image.
pub const PENDING_UPDATES_ARGUMENTS: &[&str] =
    &["--non-interactive", "--no-refresh", "list-updates"];

/// The two progress sentences `zypper` prints before a table, which carry no package. Shared with
/// the candidate reader, which reads the same tables.
pub(crate) const PREAMBLE_LINES: [&str; 2] = [
    "Loading repository data...",
    "Reading installed packages...",
];

/// The sentence `zypper list-updates` prints when nothing is pending.
const NO_UPDATES_LINE: &str = "No updates found.";

/// The header of the `list-updates` table, one cell per column.
const UPDATES_HEADER: [&str; 6] = [
    "S",
    "Repository",
    "Name",
    "Current Version",
    "Available Version",
    "Arch",
];

/// Splits one line of a `zypper` table into its trimmed cells, at the `|` between columns.
///
/// Shared by the pending-updates reader here and the candidate reader beside it, because both read
/// the same table style and must not drift on where a cell ends.
pub(crate) fn table_cells(line: &str) -> Vec<&str> {
    line.split('|').map(str::trim).collect()
}

/// Whether `line`, already trimmed, is the rule `zypper` draws under a table header: only `-` and
/// `+`.
pub(crate) fn is_table_rule(line: &str) -> bool {
    !line.is_empty() && line.bytes().all(|byte| byte == b'-' || byte == b'+')
}

/// Whether `text` is a table's status cell: at most two printable, non-space characters (`i`, `v`,
/// `i+`, `!`), or empty for a row the table marks with nothing.
pub(crate) fn is_status_marker(text: &str) -> bool {
    text.len() <= 2 && text.bytes().all(|byte| byte.is_ascii_graphic())
}

/// Whether `text` is a table's repository cell: non-empty printable ASCII, spaces allowed.
///
/// `zypper` prints a repository by its NAME (`Main Repository`), which holds spaces, so the
/// no-whitespace token test the other cells pass cannot be applied; control characters and
/// anything outside ASCII are still refused.
pub(crate) fn is_repository_label(text: &str) -> bool {
    !text.is_empty() && text.bytes().all(is_label_byte)
}

/// Whether `byte` may stand in a repository label: printable ASCII or a space.
fn is_label_byte(byte: u8) -> bool {
    byte.is_ascii_graphic() || byte == b' '
}

/// Reads the output of [`PENDING_UPDATES_ARGUMENTS`]: its exit `status` and its `stdout`.
///
/// The output is two progress sentences, then either `No updates found.` or a table whose header is
/// `S | Repository | Name | Current Version | Available Version | Arch`, a rule of `-` and `+`, and
/// one row per update. Every cell of a row must be well formed (name, versions and architecture
/// each a non-empty run of printable, non-space ASCII; the repository printable ASCII with spaces;
/// the status at most two printable characters), and a row with any other shape fails the whole
/// read, so a line this grammar has never seen is a failure to read and never a skipped update.
/// Statuses 0 and 100 are the only successes; anything else, a skipped repository (106) and a
/// failed refresh (4) included, is a failure. The answer is an empty list only for status 0 with
/// the `No updates found.` sentence and no table: an empty stdout, a header with no row, or status
/// 100 with no row is [`PendingUpdatesFailure::IncompleteOutput`], never "up to date".
///
/// `installed_version` is the table's `Current Version`; the classification of every row is
/// [`UpdateClassification::Unmeasured`] (see [`mark_security_updates`]). UNVERIFIED: the layout is
/// read from `man zypper`, confirm with `zypper --non-interactive --no-refresh list-updates` on a
/// Leap image that has pending updates.
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::Unsuccessful`] for a status other than 0 or 100;
/// [`PendingUpdatesFailure::UnrecognisedLine`] for the first line outside the grammar (a row before
/// the header, a second header, a rule before the header, a row of another shape); and
/// [`PendingUpdatesFailure::IncompleteOutput`] when the output proves neither a table of rows nor
/// the sentence that nothing is pending.
pub fn read_pending_updates(
    status: i32,
    stdout: &str,
) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
    if status != ZYPPER_OK_STATUS && status != ZYPPER_UPDATES_STATUS {
        return Err(PendingUpdatesFailure::Unsuccessful { status });
    }

    let mut updates = Vec::new();
    let mut header_seen = false;
    let mut said_none = false;
    for (index, line) in stdout.lines().enumerate() {
        let unrecognised = PendingUpdatesFailure::UnrecognisedLine { line: index + 1 };
        let text = line.trim();
        if text.is_empty() || PREAMBLE_LINES.contains(&text) {
            continue;
        }
        if text == NO_UPDATES_LINE {
            said_none = true;
            continue;
        }
        if is_table_rule(text) {
            if header_seen {
                continue;
            }
            return Err(unrecognised);
        }

        let cells = table_cells(text);
        if cells == UPDATES_HEADER {
            if header_seen {
                return Err(unrecognised);
            }
            header_seen = true;
        } else if header_seen {
            updates.push(parse_row(&cells).ok_or(unrecognised)?);
        } else {
            return Err(unrecognised);
        }
    }

    if updates.is_empty() {
        let nothing_pending = said_none && !header_seen && status == ZYPPER_OK_STATUS;
        return if nothing_pending {
            Ok(updates)
        } else {
            Err(PendingUpdatesFailure::IncompleteOutput)
        };
    }
    if said_none {
        return Err(PendingUpdatesFailure::IncompleteOutput);
    }
    Ok(updates)
}

/// Reads the six cells of one table row, or `None` when the row is not exactly six well-formed
/// cells in the `list-updates` order: status, repository, name, current version, available
/// version, architecture.
fn parse_row(cells: &[&str]) -> Option<PendingUpdate> {
    let &[marker, repository, name, current, available, architecture] = cells else {
        return None;
    };
    let tokens_are_printable = [name, current, available, architecture]
        .into_iter()
        .all(is_printable_token);
    if !tokens_are_printable || !is_status_marker(marker) || !is_repository_label(repository) {
        return None;
    }

    Some(PendingUpdate {
        package_name: name.to_owned(),
        available_version: available.to_owned(),
        installed_version: Some(current.to_owned()),
        source: repository.to_owned(),
        classification: UpdateClassification::Unmeasured,
    })
}

/// The arguments of the second, security-only cache read (issue #163).
///
/// `list-patches --category security` lists the SECURITY PATCHES the repositories' metadata says
/// apply to the host. `--non-interactive` and `--no-refresh` are as in
/// [`PENDING_UPDATES_ARGUMENTS`], so this run too refreshes nothing and contacts no repository. No
/// operand exists, so no `--` is needed. UNVERIFIED: confirm with
/// `zypper --non-interactive --no-refresh list-patches --category security` in the pinned image.
pub const SECURITY_PENDING_UPDATES_ARGUMENTS: &[&str] = &[
    "--non-interactive",
    "--no-refresh",
    "list-patches",
    "--category",
    "security",
];

/// Folds the second reading, the run of [`SECURITY_PENDING_UPDATES_ARGUMENTS`] (its exit `status`
/// and `stdout`), into the rows [`read_pending_updates`] returned (issue #163).
///
/// This family cannot say which package is a security fix from that reading, and so it leaves
/// every row [`UpdateClassification::Unmeasured`] and never `Security` or `Ordinary`. The reason
/// is the shape of the metadata: the table lists PATCHES (`Repository | Name | Category |
/// Severity | Interactive | Status | Since | Summary`, where the name is an advisory such as
/// `openSUSE-SLE-15.6-2025-1234`), and a patch names no package one to one; the packages it
/// replaces are in its description, which `list-patches` does not print. A package row cannot be
/// matched to a patch row by name, so a verdict for it would be a guess, and an `Ordinary` one
/// would say the metadata cleared a package whose metadata was not read (rules: an unread row is
/// never `Ordinary`). Reading the patch table to say "no security patch is pending" would not
/// close the gap either, because a repository without patch metadata prints the same empty
/// answer.
///
/// What the second run still earns: its exit `status` is checked, so a reading that failed (a
/// skipped repository, a failed refresh) is an error the caller logs, as on the other family. The
/// `stdout` is therefore not read at all and no byte of it can reach the verdict. Statuses 0, 100
/// and 101 are accepted, the three the commands are documented to end on. UNVERIFIED: confirm with
/// `zypper --non-interactive --no-refresh list-patches --category security; echo $?`.
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::Unsuccessful`] for a status other than 0, 100 or 101. The
/// caller then keeps the rows as they were, `Unmeasured`.
pub fn mark_security_updates(
    updates: Vec<PendingUpdate>,
    status: i32,
    _stdout: &str,
) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
    match status {
        ZYPPER_OK_STATUS | ZYPPER_UPDATES_STATUS | ZYPPER_SECURITY_STATUS => Ok(updates),
        status => Err(PendingUpdatesFailure::Unsuccessful { status }),
    }
}

/// The argument of the reboot probe, `zypper needs-rebooting`: it reports on the reboot question
/// alone and takes no operand (issue #163).
///
/// UNVERIFIED: read from `man zypper`, confirm with `zypper needs-rebooting; echo $?` in the
/// pinned Leap image.
pub const REBOOT_PROBE_ARGUMENTS: &[&str] = &["needs-rebooting"];

/// Reads the exit status of `zypper needs-rebooting`: 0 means no reboot is needed and 102
/// (`ZYPPER_EXIT_INF_REBOOT_NEEDED`) means one is.
///
/// UNVERIFIED: read from `man zypper`, confirm with `zypper needs-rebooting; echo $?` on a clean
/// container and on one where a core package was just updated.
///
/// # Errors
///
/// Returns [`RebootProbeFailure::Unsuccessful`] for any other status (127 for a program that is not
/// installed included) and never `NotRequired`.
pub fn read_reboot_probe_run(status: i32) -> Result<RebootRequirement, RebootProbeFailure> {
    match status {
        ZYPPER_OK_STATUS => Ok(RebootRequirement::NotRequired),
        ZYPPER_REBOOT_NEEDED_STATUS => Ok(RebootRequirement::Required),
        status => Err(RebootProbeFailure::Unsuccessful { status }),
    }
}

#[cfg(test)]
#[path = "../tests/suse/suse_packages_tests.rs"]
mod tests;
