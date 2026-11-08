//! Package and binary names on the Debian family, how its package manager
//! reports a failed install, and how it reports the updates it knows of.

use maran_agent_core::validation::mail::mail_password_scheme::MailPasswordScheme;

use crate::dovecot_major::DovecotMajor;
use crate::is_printable_token::is_printable_token;
use crate::key_value_server::KeyValueServer;
use crate::package_failure::PackageFailure;
use crate::pending_update::PendingUpdate;
use crate::pending_updates_failure::PendingUpdatesFailure;
use crate::reboot_probe_failure::RebootProbeFailure;
use crate::reboot_requirement::RebootRequirement;
use crate::starts_with_http_status::starts_with_http_status;
use crate::update_classification::UpdateClassification;

/// Name of the OS package that provides php-fpm for `version`, e.g.
/// `php8.3-fpm`.
///
/// Sury's Debian packages keep the dot from the version in both the package
/// name and, correspondingly, in [`crate::debian::debian_paths::php_fpm_pool_directory`].
#[must_use]
pub fn php_package(version: &str) -> String {
    format!("php{version}-fpm")
}

/// The PHP versions Sury publishes a php-fpm package for, oldest first.
///
/// Sury carries every release from 7.4 to 8.4 for the Debian releases the
/// panel supports; the list is read by the installable-versions query and by
/// the install, so a version is offered exactly when it can be installed.
#[must_use]
pub fn installable_php_versions() -> &'static [&'static str] {
    &["7.4", "8.0", "8.1", "8.2", "8.3", "8.4"]
}

/// Absolute path of the package manager binary.
///
/// Sury's repository is installed and driven through `apt-get`, the Debian
/// family's package manager (spec §4).
#[must_use]
pub fn package_manager() -> &'static str {
    "/usr/bin/apt-get"
}

/// The status `apt-get` exits with when an operation failed.
///
/// `apt-get` documents 100 for every error and 0 otherwise; a run that ended
/// on any other status (a signal, a status from the service manager) did not
/// reach apt's own error reporting, so its output is not read as apt's.
const APT_FAILURE_STATUS: i32 = 100;

/// What `apt-get` prefixes a fetch it gave up on with.
///
/// The URL follows, then two spaces, then the reason — an HTTP status line
/// (`404  Not Found`) when the source answered, the resolver's or the
/// connection's complaint when it did not. Recorded in
/// `tests/fixtures/package_manager/apt_source_{refused,unreachable}.txt`.
const APT_FETCH_FAILURE: &str = "E: Failed to fetch ";

/// The reasons `apt-get` gives for a fetch that reached no source at all.
///
/// Its HTTP method's own wording: an unresolvable name (`Temporary failure
/// resolving`, `Could not resolve`) and a connection that never completed
/// (`Could not connect to`, `Unable to connect to`, `Connection timed out`,
/// `Network is unreachable`).
const APT_UNREACHABLE_REASONS: [&str; 6] = [
    "Temporary failure resolving",
    "Could not resolve",
    "Could not connect to",
    "Unable to connect to",
    "Connection timed out",
    "Network is unreachable",
];

/// Reads a failed `apt-get install` run: its exit `status` and its combined
/// `output`.
///
/// Only the `E: Failed to fetch` lines are read, because they are the lines
/// apt writes when a source let it down and nothing else writes them. A reason
/// that opens with a three-digit HTTP status is a source that answered and
/// refused; one naming a resolver or connection failure is a source nobody
/// reached. A refusal anywhere in the run outweighs unreachable sources
/// elsewhere in it (see [`PackageFailure::SourceRefused`]). Anything else —
/// `Unable to locate package`, a dpkg failure, a held lock — is
/// [`PackageFailure::Local`].
#[must_use]
pub fn classify_package_failure(status: i32, output: &str) -> PackageFailure {
    if status != APT_FAILURE_STATUS {
        return PackageFailure::Local;
    }

    let mut verdict = PackageFailure::Local;
    for reason in output
        .lines()
        .filter_map(|line| line.strip_prefix(APT_FETCH_FAILURE))
        .filter_map(|rest| rest.split_once("  ").map(|(_, reason)| reason.trim_start()))
    {
        if starts_with_http_status(reason) {
            return PackageFailure::SourceRefused;
        }
        if APT_UNREACHABLE_REASONS
            .iter()
            .any(|marker| reason.starts_with(marker))
        {
            verdict = PackageFailure::SourceUnreachable;
        }
    }
    verdict
}

/// The arguments of the simulated upgrade that lists pending updates
/// (issue #324).
///
/// `-s` simulates: nothing is installed, removed or configured. `-q` drops the
/// progress animation. `Debug::NoLocking=true` lets the simulation run beside a
/// real package operation without taking the dpkg lock; the price is a read
/// that may be inconsistent, which fails closed in [`read_pending_updates`].
/// `dist-upgrade` is the verb that also lists upgrades needing a new dependency,
/// which plain `upgrade` holds back. No operand exists, so nothing a caller
/// chooses can reach the program, and no `--` is needed.
pub const PENDING_UPDATES_ARGUMENTS: &[&str] =
    &["-s", "-q", "-o", "Debug::NoLocking=true", "dist-upgrade"];

/// The arguments of the run that lists the package indexes apt has on disk.
///
/// `apt-get indextargets` prints one line per index file that EXISTS, and
/// nothing at all against an empty lists directory (recorded in
/// `tests/fixtures/package_manager/apt_indextargets_empty.txt`), which is what
/// tells a catalogue that was never built from one that is up to date.
pub const CATALOGUE_PROBE_ARGUMENTS: &[&str] = &["indextargets", "--format", "$(FILENAME)"];

/// Reads the run of [`CATALOGUE_PROBE_ARGUMENTS`].
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::Unsuccessful`] for any status but 0, and
/// [`PendingUpdatesFailure::CatalogueNotBuilt`] when no index file is listed.
pub fn read_catalogue_probe(status: i32, stdout: &str) -> Result<(), PendingUpdatesFailure> {
    if status != 0 {
        return Err(PendingUpdatesFailure::Unsuccessful { status });
    }
    if stdout.trim().is_empty() {
        return Err(PendingUpdatesFailure::CatalogueNotBuilt);
    }
    Ok(())
}

/// What a simulation prints before it has said anything about a package.
const APT_PROGRESS_LINES: [&str; 4] = [
    "Reading package lists",
    "Building dependency tree",
    "Reading state information",
    "Calculating upgrade",
];

/// What opens each list apt prints under a heading: the packages that would be
/// installed, upgraded, removed or kept back, and the ones left over.
const APT_LIST_HEADINGS: [&str; 3] = [
    "The following ",
    "Suggested packages:",
    "Recommended packages:",
];

/// The one hint apt prints after the leftover list.
const APT_AUTOREMOVE_HINT: &str = "Use 'apt autoremove' to remove ";

/// Reads the output of [`PENDING_UPDATES_ARGUMENTS`]: its exit `status` and its
/// `stdout`.
///
/// `apt-get` exits 0 for a simulation that ran and 100 for any error, so any
/// other status is a failure. Only `Inst` lines that carry an installed
/// version, in square brackets, become rows: a line without one is a package
/// being NEWLY installed to satisfy an upgrade, which is not an update of
/// anything on the host. `Conf`, `Remv` and `Purg` lines are the other halves
/// of the same transaction and are ignored by rule. Every other line must be
/// one apt is known to print — its progress lines, a list heading, the
/// indented names under one, the autoremove hint, the closing summary — and
/// anything else fails the read, so a line this grammar has never seen is a
/// failure to read and never a skipped update.
///
/// The closing summary (`N upgraded, N newly installed, N to remove and N not
/// upgraded.`) is required: output without it was cut short, and its absence is
/// [`PendingUpdatesFailure::IncompleteOutput`]. A package apt keeps back is
/// listed under a heading and has no `Inst` line, so it is not reported; the
/// summary's "not upgraded" count is where it shows.
///
/// # Errors
///
/// Returns [`PendingUpdatesFailure::Unsuccessful`] for a status other than 0,
/// [`PendingUpdatesFailure::UnrecognisedLine`] for the first line outside the
/// grammar or an `Inst` line that does not parse, and
/// [`PendingUpdatesFailure::IncompleteOutput`] when the summary is missing.
pub fn read_pending_updates(
    status: i32,
    stdout: &str,
) -> Result<Vec<PendingUpdate>, PendingUpdatesFailure> {
    if status != 0 {
        return Err(PendingUpdatesFailure::Unsuccessful { status });
    }

    let mut updates = Vec::new();
    let mut closed = false;
    for (index, line) in stdout.lines().enumerate() {
        let unrecognised = PendingUpdatesFailure::UnrecognisedLine { line: index + 1 };
        if let Some(rest) = line.strip_prefix("Inst ") {
            if let Some(update) = parse_inst(rest).ok_or(unrecognised)? {
                updates.push(update);
            }
        } else if is_summary(line) {
            closed = true;
        } else if !is_ignored(line) {
            return Err(unrecognised);
        }
    }

    if closed {
        Ok(updates)
    } else {
        Err(PendingUpdatesFailure::IncompleteOutput)
    }
}

/// Whether `line` is one apt prints that carries no update: a blank line, the
/// other halves of a transaction, a progress line, a heading, the indented
/// names under it, or the autoremove hint.
fn is_ignored(line: &str) -> bool {
    line.trim().is_empty()
        || line.starts_with(char::is_whitespace)
        || line.starts_with("Conf ")
        || line.starts_with("Remv ")
        || line.starts_with("Purg ")
        || line.starts_with(APT_AUTOREMOVE_HINT)
        || APT_PROGRESS_LINES
            .iter()
            .any(|progress| line.starts_with(progress))
        || APT_LIST_HEADINGS
            .iter()
            .any(|heading| line.starts_with(heading))
}

/// The words of the summary apt closes a simulation with, by position, between
/// the four counts: `N upgraded, N newly installed, N to remove and N not
/// upgraded.`
const SUMMARY_WORDS: [(usize, &str); 8] = [
    (1, "upgraded,"),
    (3, "newly"),
    (4, "installed,"),
    (6, "to"),
    (7, "remove"),
    (8, "and"),
    (10, "not"),
    (11, "upgraded."),
];

/// Where the four counts sit in the summary's twelve words.
const SUMMARY_COUNTS: [usize; 4] = [0, 2, 5, 9];

/// Whether `line` is the summary apt closes a simulation with:
/// `N upgraded, N newly installed, N to remove and N not upgraded.`
fn is_summary(line: &str) -> bool {
    let words: Vec<&str> = line.split(' ').collect();
    words.len() == 12
        && SUMMARY_COUNTS.iter().all(|position| {
            let count = words[*position];
            !count.is_empty() && count.bytes().all(|byte| byte.is_ascii_digit())
        })
        && SUMMARY_WORDS
            .iter()
            .all(|(position, word)| words[*position] == *word)
}

/// Reads what follows `Inst ` on one line, or `None` when it is not in the
/// form `NAME [INSTALLED] (VERSION ORIGINS [ARCH])` plus an optional trailing
/// `[REVERSE DEPENDENCIES]` — the form recorded in
/// `tests/fixtures/package_manager/apt_dist_upgrade_with_updates.txt`.
///
/// `Some(None)` is a well-formed line for a package with no installed version
/// (a new dependency), which is not an update. The origins are everything
/// between the version and the architecture bracket, kept as apt prints them.
fn parse_inst(rest: &str) -> Option<Option<PendingUpdate>> {
    let (name, rest) = rest.split_once(' ')?;
    if !is_printable_token(name) {
        return None;
    }

    let (installed, rest) = match rest.strip_prefix('[') {
        Some(bracketed) => {
            let (installed, after) = bracketed.split_once("] ")?;
            if !is_printable_token(installed) {
                return None;
            }
            (Some(installed), after)
        }
        None => (None, rest),
    };

    let group = rest.strip_prefix('(')?;
    let close = group.char_indices().find_map(|(index, character)| {
        let tail = &group[index + 1..];
        (character == ')' && (tail.is_empty() || tail.starts_with(" ["))).then_some(index)
    })?;
    let (inside, tail) = (&group[..close], &group[close + 1..]);
    if !(tail.is_empty() || (tail.starts_with(" [") && tail.ends_with(']'))) {
        return None;
    }

    let (version, origins) = inside.split_once(' ').unwrap_or((inside, ""));
    if !is_printable_token(version) {
        return None;
    }
    let source = match origins.rsplit_once(" [") {
        Some((before, arch)) if arch.ends_with(']') => before,
        _ => origins,
    };
    if source.chars().any(char::is_control) {
        return None;
    }

    Some(installed.map(|installed| PendingUpdate {
        package_name: name.to_owned(),
        available_version: version.to_owned(),
        installed_version: Some(installed.to_owned()),
        source: source.to_owned(),
        classification: classify_origins(source),
    }))
}

/// The suffix of a suite name apt's security channel uses: `noble-security`
/// on Ubuntu, `stable-security` on current Debian.
const SECURITY_SUITE_SUFFIX: &str = "-security";

/// Classifies an update by the origin list apt printed for it (issue #163).
///
/// The list is `ORIGIN:RELEASE/SUITE` entries separated by `, `, as apt prints
/// them (`Ubuntu:24.04/noble-updates, Ubuntu:24.04/noble-security`); the suite
/// is what follows the last `/` of an entry. One suite ending `-security` makes
/// the update [`UpdateClassification::Security`], because the very version that
/// would be installed is published by the security channel; any other list,
/// an empty one included, is [`UpdateClassification::Ordinary`]. Apt printed the
/// origins in the output being read, so this is a measured answer and never
/// `Unmeasured`.
fn classify_origins(source: &str) -> UpdateClassification {
    let is_security = source.split(", ").any(|origin| {
        origin
            .rsplit('/')
            .next()
            .is_some_and(|suite| suite.ends_with(SECURITY_SUITE_SUFFIX))
    });
    if is_security {
        UpdateClassification::Security
    } else {
        UpdateClassification::Ordinary
    }
}

/// Reads the exit status of a reboot probe run, which this family does not
/// have: its probe is a marker file and presence is the whole answer
/// (issue #163).
///
/// # Errors
///
/// Always [`RebootProbeFailure::NotAProgramProbe`]: there is no run whose status
/// could mean anything, and answering `NotRequired` for one would be a guess.
pub fn read_reboot_probe_run(_status: i32) -> Result<RebootRequirement, RebootProbeFailure> {
    Err(RebootProbeFailure::NotAProgramProbe)
}

/// The packages that install the mail stack on the Debian family.
///
/// See [`MailFacts::mail_packages`](crate::MailFacts::mail_packages) for what the value is for and how far it is verified.
#[must_use]
pub fn mail_packages() -> &'static [&'static str] {
    &[
        "postfix",
        "postfix-lmdb",
        "dovecot-core",
        "dovecot-imapd",
        "dovecot-pop3d",
        "dovecot-lmtpd",
        "dovecot-sieve",
        "rspamd",
    ]
}

/// The packages that install ClamAV on the Debian family.
///
/// See [`MailFacts::antivirus_packages`](crate::MailFacts::antivirus_packages) for what the value is for and how far it is verified.
#[must_use]
pub fn antivirus_packages() -> &'static [&'static str] {
    &["clamav", "clamav-daemon", "clamav-freshclam"]
}

/// The Postfix lookup-table type on the Debian family.
///
/// See [`MailFacts::postfix_map_type`](crate::MailFacts::postfix_map_type) for what the value is for and how far it is verified.
#[must_use]
pub fn postfix_map_type() -> &'static str {
    "lmdb"
}

/// The mailbox password scheme on the Debian family.
///
/// See [`MailFacts::mail_password_scheme`](crate::MailFacts::mail_password_scheme) for what the value is for and how far it is verified.
#[must_use]
pub fn mail_password_scheme() -> MailPasswordScheme {
    MailPasswordScheme::Argon2id
}

/// The Dovecot generation of a release of the Debian family.
///
/// See [`MailFacts::dovecot_major`](crate::MailFacts::dovecot_major) for what the value is for and how far it is verified.
#[must_use]
pub fn dovecot_major(version_id: &str) -> Option<DovecotMajor> {
    match version_id {
        "12" | "22.04" | "24.04" => Some(DovecotMajor::V23),
        "13" => Some(DovecotMajor::V24),
        _ => None,
    }
}

/// The Redis-protocol server of a release of the Debian family.
///
/// See [`MailFacts::key_value_server`](crate::MailFacts::key_value_server) for what the value is for and how far it is verified.
#[must_use]
pub fn key_value_server(version_id: &str) -> Option<KeyValueServer> {
    match version_id {
        "12" | "13" | "22.04" | "24.04" => Some(KeyValueServer {
            package: "redis-server",
            service: "redis-server",
            user: "redis",
            config_file: "/etc/redis/redis.conf",
        }),
        _ => None,
    }
}

#[cfg(test)]
#[path = "../tests/debian/debian_packages_tests.rs"]
mod tests;
