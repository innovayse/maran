//! Everything the host needs to follow one site's log.

use std::ffi::OsString;
use std::path::PathBuf;

use maran_agent_core::validation::system::name::AccountName;

/// One site's log, named as a directory plus a file name rather than as a path.
///
/// Split deliberately, and it is the whole security shape of the tail. The
/// directory has been through `resolve_in_home` and is opened ONCE, for the
/// life of the stream; the file is then reached through that descriptor by
/// name. A single `PathBuf` would have to be re-resolved on every poll, which
/// is the race `resolve_in_home`'s own documentation says not to reintroduce:
/// `rmdir logs && ln -s /somewhere logs` between two polls redirects every
/// later open, and `O_NOFOLLOW` never sees an intermediate component.
///
/// The account travels with it because the host must prove the file it opened
/// belongs to that account. A log the customer replaced with a hardlink to a
/// file outside their home passes every path check ever written — the path IS
/// inside the home — and is caught only by looking at the inode.
#[derive(Debug, Clone)]
pub struct LogTailRequest {
    /// The account whose log this is. The opened file must be owned by it.
    pub account: AccountName,
    /// The account's log directory, canonical, as `resolve_in_home` returned it.
    pub directory: PathBuf,
    /// The log's file name — one path component, derived by
    /// [`super::site_paths::SitePaths`] from a validated `Domain` and never
    /// supplied by a caller.
    pub file_name: OsString,
    /// How many historical lines to send, already clamped by
    /// `tail_site_log` to `MAXIMUM_HISTORY_LINES`.
    pub history_lines: u32,
}
