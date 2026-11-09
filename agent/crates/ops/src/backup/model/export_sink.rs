//! Where the messages of an export go.

use crate::backup::model::export_stopped::ExportStopped;
use crate::backup::package::signed_manifest::SignedManifest;

/// The receiving end of [`export_backup`](crate::backup::export_backup).
///
/// The service layer implements it over a bounded channel behind the response
/// stream; a test implements it over a vector. The contract is the order: the
/// header exactly once, then zero or more non-empty chunks.
pub trait ExportSink {
    /// Delivers the signed manifest, before any chunk.
    ///
    /// # Errors
    ///
    /// [`ExportStopped`] when the consumer is gone or stopped reading.
    fn header(&mut self, signed: &SignedManifest) -> Result<(), ExportStopped>;

    /// Delivers the next slice of the artifact, in order.
    ///
    /// # Errors
    ///
    /// [`ExportStopped`] when the consumer is gone or stopped reading.
    fn chunk(&mut self, data: &[u8]) -> Result<(), ExportStopped>;
}
