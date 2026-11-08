//! The pin as the fleet directory's `panel.pem` holds it.

use std::path::{Path, PathBuf};
use std::sync::{PoisonError, RwLock};

use tokio::sync::Mutex;

use super::fleet_identity_error::FleetIdentityError;
use super::panel_pin::PanelPin;
use super::pin_source::PinSource;
use super::pinned_panel_certificate::PinnedPanelCertificate;

/// A [`PinSource`] that re-reads `panel.pem` from the fleet directory on every
/// [`PinSource::refresh`] and answers [`PinSource::current`] from what the last refresh read.
///
/// The file IS the access list, so it is re-read at every handshake and every request and the
/// copy in memory is only ever as old as the latest refresh: deleting the file (`maran node
/// leave`) or replacing it with another panel's certificate takes effect at the next handshake
/// and the next request without a restart. The read is a directory check, a small file read and
/// a certificate parse, all blocking, so it runs on the blocking pool and never on an async
/// worker; the synchronous TLS verifier, which cannot wait for it, reads the copy.
///
/// Refreshes take turns. Two concurrent ones could otherwise store their results out of order,
/// and a read that started before the file was removed would put the pin back after the read
/// that saw it gone. Taking turns means every refresh stores a read that began after the
/// previous refresh stored its own, so a request that begins after the removal cannot be
/// admitted by an older read.
///
/// A missing file is the ordinary state of a node that is not enrolled or has left, and answers
/// `None` quietly. Anything else wrong with the pin (loose directory permissions, a file that is
/// not one certificate, a read that did not finish) also answers `None`, because a listener that
/// cannot read its pin admits nobody, and is logged at `warn` because that state needs an
/// operator.
#[derive(Debug)]
pub struct PanelPinFile {
    /// The fleet directory.
    directory: PathBuf,
    /// The uid that must own the directory: the user the agent runs as.
    owner_uid: u32,
    /// What the latest refresh read; `None` before the first refresh, which admits nobody.
    read: RwLock<Option<PinnedPanelCertificate>>,
    /// Held for the whole of a refresh, so refreshes take turns.
    refreshing: Mutex<()>,
}

impl PanelPinFile {
    /// A source reading `panel.pem` from `directory`, which must be owned by `owner_uid`.
    ///
    /// Nothing is pinned until the first [`PinSource::refresh`].
    #[must_use]
    pub fn new(directory: PathBuf, owner_uid: u32) -> Self {
        Self {
            directory,
            owner_uid,
            read: RwLock::new(None),
            refreshing: Mutex::new(()),
        }
    }

    /// Reads and parses the pin file. Blocking: call it from the blocking pool only.
    fn read_pin(directory: &Path, owner_uid: u32) -> Option<PinnedPanelCertificate> {
        let pin = match PanelPin::read(directory, owner_uid) {
            Ok(pin) => pin,
            Err(FleetIdentityError::FileMissing { .. }) => return None,
            Err(error) => {
                tracing::warn!(
                    %error,
                    "the pinned panel certificate cannot be read; no fleet peer is admitted"
                );
                return None;
            }
        };

        match PinnedPanelCertificate::from_pem(&pin.certificate_pem) {
            Ok(certificate) => Some(certificate),
            Err(error) => {
                tracing::warn!(
                    %error,
                    "the pinned panel certificate is unusable; no fleet peer is admitted"
                );
                None
            }
        }
    }
}

#[tonic::async_trait]
impl PinSource for PanelPinFile {
    /// The certificate the latest refresh read, or `None` when there was none or it could not
    /// be used.
    ///
    /// A lock poisoned by a panicking holder is taken anyway: the guarded value is an `Option`
    /// that a panic cannot leave half-written.
    fn current(&self) -> Option<PinnedPanelCertificate> {
        self.read
            .read()
            .unwrap_or_else(PoisonError::into_inner)
            .clone()
    }

    /// Re-reads `panel.pem` on the blocking pool and stores the result. A read task that fails
    /// to finish stores `None`: a pin that could not be read admits nobody.
    async fn refresh(&self) {
        let _turn = self.refreshing.lock().await;

        let directory = self.directory.clone();
        let owner_uid = self.owner_uid;
        let pin = match tokio::task::spawn_blocking(move || Self::read_pin(&directory, owner_uid))
            .await
        {
            Ok(pin) => pin,
            Err(error) => {
                tracing::warn!(
                    %error,
                    "reading the pinned panel certificate did not finish; no fleet peer is admitted"
                );
                None
            }
        };

        *self.read.write().unwrap_or_else(PoisonError::into_inner) = pin;
    }
}

#[cfg(test)]
#[path = "../tests/fleet/panel_pin_file_tests.rs"]
mod tests;
