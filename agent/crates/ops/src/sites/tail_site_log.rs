//! Following one site's log, bounded at every end.

use maran_agent_core::validation::system::name::AccountName;
use maran_agent_core::validation::web::domain::Domain;

use crate::sites::log_sink::LogSink;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::site_log_kind::SiteLogKind;
use crate::sites::model::site_paths::SitePaths;
use crate::sites::model::tail_end::TailEnd;
use crate::sites::{SiteMaintenanceHost, SitesOpError};

/// The most historical lines a tail will ever send, whatever was asked for.
///
/// `sites.proto` states the cap as part of the contract ("Capped by the agent
/// at 1000 regardless of the requested value"), and it is applied here rather
/// than in the service because it is a property of the operation.
///
/// It caps what is SENT. It is not on its own a bound on what is read: the log
/// is a file the account controls the size of, and a thousand lines could be
/// at the end of fifty gigabytes. The host holds the matching byte budget, and
/// the two together are what make a tail safe to run as root.
pub const MAXIMUM_HISTORY_LINES: u32 = 1000;

/// Sends the tail of one site's log to `emit`, then follows what is appended.
///
/// Returns why the tail ended ([`TailEnd`]), which the service turns into a
/// terminal message when the ending was the agent's decision rather than the
/// client's.
///
/// Lines go to `sink`, which also answers whether anyone is still listening —
/// a trait rather than a channel for the same reason `install_php_version`'s
/// progress is a callback: this function stays synchronous and testable
/// against a fake, and the service layer decides what a stream does with each
/// line.
///
/// **The tail is bounded at every end.** The history is clamped to
/// [`MAXIMUM_HISTORY_LINES`] here, before the host is asked for anything; the
/// host bounds how many BYTES it reads to find those lines, because the file
/// belongs to the account and the account can make it as large as its quota
/// allows; the follow stops when the sink refuses a line, when it reports
/// nobody is listening, or when it has been idle too long.
///
/// The log directory is `/var/log/maran/sites/<account>` — **outside every
/// home** — and is handed to the host as a directory to hold open, with the
/// file named separately by [`SitePaths`]: no request can name a path, and no
/// swap of an intermediate component can redirect a tail that is already
/// running.
///
/// There is no `resolve_in_home` call here any more, and its absence is the
/// point rather than an omission. These files used to live in the account's
/// home, where the containment that mattered was "is this still inside the
/// home?"; the root nginx master opened them there without `O_NOFOLLOW`, which
/// a customer turned into root code execution with one `ln -s`
/// (`docs/superpowers/notes/2026-09-09-site-logs-threat-note.md`). Their
/// containment now comes from an ancestor chain no unprivileged uid can write
/// to or traverse — `/var/log/maran` is `root:maran 0750`,
/// `/var/log/maran/sites` and each account's directory below it are `root:root
/// 0750` — so asking whether the path is inside a home would answer a question
/// nobody is asking. What the host still does, unchanged, is pin the directory
/// descriptor and prove ownership and file kind on the inode; those checks now
/// guard ROOT's files.
///
/// # Errors
///
/// Returns [`SitesOpError::LogUnreadable`] when the log directory is missing or
/// is not a directory root owns, and when the log is not a regular file root
/// owns with a single link, or cannot be read.
pub fn tail_site_log(
    host: &dyn SiteMaintenanceHost,
    account: &AccountName,
    domain: &Domain,
    kind: SiteLogKind,
    history_lines: u32,
    sink: &mut dyn LogSink,
) -> Result<TailEnd, SitesOpError> {
    // One seam, not two. This used to be generic over `SiteHost +
    // SiteMaintenanceHost` because the tail needed the home-containment check
    // that `SiteHost` owns as well as the read that `SiteMaintenanceHost` owns.
    // The logs are no longer in a home, so the containment check is gone and
    // with it the reason to name two traits — and a bound nothing uses is worse
    // than none, because it tells the next reader this function reaches the
    // filesystem in a way it no longer does.
    let named = SitePaths::for_site(account, domain);
    let log = match kind {
        SiteLogKind::Access => named.access_log,
        SiteLogKind::Error => named.error_log,
    };

    // Named as a directory rather than as the file: a site that has served no
    // request yet has no access log, and requiring the file to exist would make
    // "no traffic" indistinguishable from a path the host must refuse. The host
    // holds this directory OPEN for the life of the tail and reaches the log
    // through that descriptor, which is what stops the path being swapped
    // between two polls.
    let directory = SitePaths::log_directory_for(account);

    let file_name = match log.file_name() {
        Some(name) => name.to_owned(),
        // `SitePaths` builds both log paths by joining a file name onto a
        // directory, so this is unreachable; it is an error rather than an
        // unwrap because the workspace has no unwrap (rules/rust.md).
        None => {
            return Err(SitesOpError::LogUnreadable {
                path: log.display().to_string(),
            });
        }
    };

    host.tail_log(
        &LogTailRequest {
            directory,
            file_name,
            // Clamped HERE and not in the service, so the ceiling cannot be
            // bypassed by calling the operation from somewhere else.
            history_lines: history_lines.min(MAXIMUM_HISTORY_LINES),
        },
        sink,
    )
}

#[cfg(test)]
#[path = "../tests/sites/tail_site_log_tests.rs"]
mod tests;
