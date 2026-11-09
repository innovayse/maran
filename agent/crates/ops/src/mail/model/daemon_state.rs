//! One daemon of the mail stack and what the host says about it.

use crate::mail::model::mail_daemon::MailDaemon;

/// The observed state of one mail daemon.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct DaemonState {
    /// Which daemon this describes.
    pub daemon: MailDaemon,
    /// Whether the daemon's program is on the host. A package the installer could
    /// not install (the spam filter, on a family without a distribution package)
    /// reads as `false` here, which is why enabling mail can refuse with a name.
    pub installed: bool,
    /// Whether the service manager reports the daemon's unit active.
    pub active: bool,
}
