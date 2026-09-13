//! Everything the host needs to follow one site's log.

use std::ffi::OsString;
use std::path::PathBuf;

/// One site's log, named as a directory plus a file name rather than as a path.
///
/// Split deliberately, and it is the whole security shape of the tail. The
/// directory is opened ONCE, for the life of the stream; the file is then
/// reached through that descriptor by name. A single `PathBuf` would have to be
/// re-resolved on every poll, which is the race `validation::fs::path`'s own
/// documentation says not to reintroduce: `rmdir DIR && ln -s /somewhere DIR`
/// between two polls redirects every later open, and `O_NOFOLLOW` never
/// sees an intermediate component.
///
/// **The account no longer travels with it.** It used to, because the host had
/// to prove the opened file belonged to that account — the logs were in the
/// account's home and the account owned them. They are now root's, at
/// `/var/log/maran/sites/<account>`, so the uid the host checks is a constant
/// and not a property of the request; carrying an account here would be a field
/// nothing reads, which is how a struct comes to describe a check that stopped
/// happening. The account is still what NAMES the directory —
/// `SitePaths::log_directory_for` — it is simply not part of the answer to "may
/// this file be read?".
#[derive(Debug, Clone)]
pub struct LogTailRequest {
    /// The directory holding this account's site logs, root-owned.
    pub directory: PathBuf,
    /// The log's file name — one path component, derived by
    /// [`super::site_paths::SitePaths`] from a validated `Domain` and never
    /// supplied by a caller.
    pub file_name: OsString,
    /// How many historical lines to send, already clamped by
    /// `tail_site_log` to `MAXIMUM_HISTORY_LINES`.
    pub history_lines: u32,
}
