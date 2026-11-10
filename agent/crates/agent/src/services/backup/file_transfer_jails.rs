//! The jail mounts a restore refreshes, answered by the SFTP and FTPS areas.

use std::sync::Arc;

use maran_agent_core::validation::system::name::AccountName;
use maran_distro::DistroAdapter;
use maran_ops::backup::{BackupError, JailMounts};
use maran_ops::ftps::{self, FtpsHost};
use maran_ops::sftp::{self, SftpHost};

/// Answers [`JailMounts`] by asking `ops::sftp` and `ops::ftps` to mount the
/// account's current home into their jails again.
///
/// **This composition is the whole reason the seam exists.** `ops::backup` does
/// not import another `ops` area, so it declares what it needs as a trait and
/// this service layer wires the two protocols in, exactly as
/// [`DbHostCatalog`](crate::services::backup::db_host_catalog::DbHostCatalog)
/// wires the database area.
///
/// Both protocols are always asked, SFTP first, even when the first fails: a
/// jail left showing the displaced home is lost data for whoever logs in to it,
/// and one protocol's failure is no reason to leave the other's stale. A failure
/// of either is [`BackupError::JailsNotRefreshed`]; the area's own error is
/// logged here, once, at the boundary that handles it, and not carried across —
/// `ops::backup` has one error enum for its whole area.
pub struct FileTransferJails<S, F> {
    /// The machine the SFTP jail's unit is restarted on.
    sftp: Arc<S>,
    /// The machine the FTPS jail's unit is restarted on.
    ftps: Arc<F>,
    /// Where the unit directory and the service manager come from.
    distro: &'static dyn DistroAdapter,
}

impl<S, F> FileTransferJails<S, F> {
    /// Creates the seam around the two hosts and the platform facts.
    #[must_use]
    pub fn new(sftp: S, ftps: F, distro: &'static dyn DistroAdapter) -> Self {
        Self {
            sftp: Arc::new(sftp),
            ftps: Arc::new(ftps),
            distro,
        }
    }
}

impl<S: SftpHost + Send + Sync, F: FtpsHost + Send + Sync> JailMounts for FileTransferJails<S, F> {
    /// Refreshes the SFTP jail, then the FTPS jail, of `account`.
    ///
    /// # Errors
    ///
    /// Returns [`BackupError::JailsNotRefreshed`] when either protocol's jail
    /// exists and could not be mounted onto the current home.
    fn refresh(&self, account: &AccountName) -> Result<(), BackupError> {
        let sftp = sftp::refresh_account_jail(self.sftp.as_ref(), self.distro, account);
        let ftps = ftps::refresh_account_jail(self.ftps.as_ref(), self.distro, account);

        if let Err(error) = &sftp {
            tracing::error!(account = %account.as_str(), %error, "the SFTP jail could not follow the restored home");
        }
        if let Err(error) = &ftps {
            tracing::error!(account = %account.as_str(), %error, "the FTPS jail could not follow the restored home");
        }

        if sftp.is_err() || ftps.is_err() {
            return Err(BackupError::JailsNotRefreshed);
        }

        Ok(())
    }
}
