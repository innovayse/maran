//! The seam for the two site operations that do not edit a vhost.

use crate::safe_write::model::{Reload, Validator};
use crate::sites::SitesOpError;
use crate::sites::log_sink::LogSink;
use crate::sites::model::log_tail_request::LogTailRequest;
use crate::sites::model::tail_end::TailEnd;

/// Reloading the web server, and reading a site's log.
///
/// A second seam beside [`super::SiteHost`] rather than two more methods on
/// it, because `SiteHost` is implemented by `crate::ssl`'s host as well — the
/// SSL area rewires vhosts and therefore needs the vhost half of it. Tailing a
/// customer's log and reloading nginx are not things certificate installation
/// does, and putting them on the shared seam would oblige that host to answer
/// for operations it never performs. The one implementation that touches the
/// machine is [`super::ProcessSiteHost`].
pub trait SiteMaintenanceHost: Send + Sync {
    /// Runs the validator and then the reload, writing nothing.
    ///
    /// The batching reload `sites.proto` offers, and the only place the write
    /// protocol's two commands are used without a swap between them.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::NginxValidation`] when the validator refuses
    /// the configuration already on disk, and [`SitesOpError::ReloadFailed`]
    /// when the reload refuses it. Nothing is rolled back, because nothing was
    /// changed.
    fn validate_and_reload(
        &self,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), SitesOpError>;

    /// Sends the historical tail of `request`'s log to `sink`, then follows
    /// what is appended to it.
    ///
    /// The implementation opens `request.directory` once and reaches the file
    /// through that descriptor on every poll, so the path cannot be swapped
    /// underneath a running tail; it proves the opened inode is a regular file
    /// owned by `request.account` with no second link; and it bounds every
    /// read, because the file belongs to the account and the account can make
    /// it as large as its quota allows.
    ///
    /// The follow ends when `sink` stops listening, when it refuses a line, or
    /// when the tail has been idle for the implementation's maximum — never
    /// only on the arrival of a line, since a log that never gets one is
    /// exactly the case that would otherwise leak the thread. Which of those
    /// happened comes back as the [`TailEnd`], because two of the three are the
    /// agent's decision and the operator has to be able to see them.
    ///
    /// Implementations MUST be called from `tokio::task::spawn_blocking`: the
    /// follow sleeps between reads and lives as long as the client does.
    ///
    /// # Errors
    ///
    /// Returns [`SitesOpError::LogUnreadable`] when the directory or the log
    /// cannot be opened as what it must be. A log that does not exist yet is
    /// not an error — a site that has served no request has no access log —
    /// and yields no lines.
    fn tail_log(
        &self,
        request: &LogTailRequest,
        sink: &mut dyn LogSink,
    ) -> Result<TailEnd, SitesOpError>;
}
