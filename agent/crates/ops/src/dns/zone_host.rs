//! The seam between the zone operations and the files a zone lives in.

use std::path::Path;

use crate::dns::dns_error::DnsError;
use crate::dns::dns_host::DnsHost;
use crate::safe_write::model::{Reload, Validator};

/// What the zone operations need beyond [`DnsHost`]: writing a zone file at a
/// mode the daemon can read, listing the zone directory and measuring its room.
///
/// A second trait rather than more methods on [`DnsHost`] because the daemon
/// operations never touch a zone directory, and a fake of the daemon would
/// otherwise have to answer for files it has no use for. Running `rndc` and the
/// zone checker is [`DnsHost::run`]; removing a file is [`DnsHost::remove_file`].
pub trait ZoneHost: DnsHost {
    /// Writes `contents` to `target` through the config-write protocol at mode
    /// `0644`, then validates and loads it, and restores the previous content
    /// (or removes the file, when there was none) if either refuses.
    ///
    /// The mode is the reason this is not [`DnsHost::write_config`]: that one
    /// leaves the file at the protocol's private default, and the daemon reads
    /// zone files AFTER dropping to its unprivileged user. A zone's content is
    /// published to the whole internet by the daemon, so a world-readable file
    /// discloses nothing.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ZoneRejected`] when the zone checker exits non-zero,
    /// [`DnsError::ControlRefused`] when the load command does,
    /// [`DnsError::SpawnFailed`] when either could not be started,
    /// [`DnsError::Bound`] when either outran its deadline and
    /// [`DnsError::ConfigWrite`] for the protocol's mechanical failures. The
    /// previous content has been restored in every case.
    fn write_zone_file(
        &self,
        target: &Path,
        contents: &str,
        validator: &Validator<'_>,
        reload: &Reload<'_>,
    ) -> Result<(), DnsError>;

    /// The names of the entries in `directory`, in no particular order. A
    /// directory that is not there is an empty list: no zone was ever written.
    ///
    /// # Errors
    ///
    /// Returns [`DnsError::ZoneDirectoryUnreadable`] when the directory exists
    /// and cannot be listed.
    fn zone_file_names(&self, directory: &Path) -> Result<Vec<String>, DnsError>;

    /// How many bytes an unprivileged writer could still add on the filesystem
    /// `directory` sits on.
    ///
    /// # Errors
    ///
    /// Returns the I/O error when the filesystem cannot be measured, which the
    /// caller must treat as a refusal.
    fn available_bytes(&self, directory: &Path) -> std::io::Result<u64>;
}
